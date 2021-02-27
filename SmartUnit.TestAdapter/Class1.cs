using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace SmartUnit.TestAdapter
{
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(ExecutorUri)]
    [ExtensionUri(ExecutorUri)]
    [Category("managed")]
    public class TestRunner : ITestDiscoverer, ITestExecutor
    {
        public const string ExecutorUri = "executor://SmartUnitExecutor";

        private CancellationTokenSource cancellationToken = new CancellationTokenSource();

        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            foreach (var source in sources)
            {
                var sourceAssemblyPath = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
                logger.SendMessage(TestMessageLevel.Informational, $"Discovered source '{source}'");

                var assembly = Assembly.LoadFrom(sourceAssemblyPath);
                var tests = assembly.GetTypes()
                    .SelectMany(s => s.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    .Where(w => w.GetCustomAttribute<AssertionAttribute>() is not null)
                    .ToList();

                foreach (var test in tests)
                {
                    var assertionAttribute = test.GetCustomAttribute<AssertionAttribute>()!;
                    logger.SendMessage(TestMessageLevel.Informational, $"Discovered test '{test.DeclaringType.FullName + "." + test.Name}'");

                    discoverySink.SendTestCase(new TestCase(test.DeclaringType.FullName + "." + test.Name, new Uri(ExecutorUri), source)
                    {
                        DisplayName = string.IsNullOrEmpty(assertionAttribute.Name) ? test.Name : assertionAttribute.Name
                    });
                }
            }
        }

        public void Cancel()
        {
            cancellationToken.Cancel();
        }

        public async void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            foreach (var testCase in tests)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                frameworkHandle.RecordStart(testCase);

                var sourceAssemblyPath = Path.IsPathRooted(testCase.Source) ? testCase.Source : Path.Combine(Directory.GetCurrentDirectory(), testCase.Source);
                var assembly = Assembly.LoadFrom(sourceAssemblyPath);

                var nameSeparatorIndex = testCase.FullyQualifiedName.LastIndexOf('.');
                var typeName = testCase.FullyQualifiedName.Substring(0, nameSeparatorIndex);

                var testClass = assembly.GetType(typeName);
                var testMethod = testClass.GetMethod(testCase.FullyQualifiedName.Substring(nameSeparatorIndex + 1));

                try
                {
                    if (testMethod.Name.StartsWith('<'))
                    {
                        await RunNestedTest(testMethod);
                    }
                    else
                    {
                        await RunTest(testMethod);
                    }
                    frameworkHandle.RecordEnd(testCase, TestOutcome.Passed);
                }
                catch
                {
                    frameworkHandle.RecordEnd(testCase, TestOutcome.Failed);
                }
            }
        }

        public async void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            foreach (var source in sources)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var sourceAssemblyPath = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
                var assembly = Assembly.LoadFrom(sourceAssemblyPath);

                var tests = assembly.GetTypes()
                    .SelectMany(s => s.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    .Where(w => w.GetCustomAttribute<AssertionAttribute>() is not null)
                    .ToList();
                foreach (var test in tests)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var testCase = new TestCase()
                    {
                        FullyQualifiedName = test.DeclaringType.FullName + "." + test.Name,
                        DisplayName = test.Name,
                        Source = source
                    };

                    frameworkHandle.RecordStart(testCase);

                    try
                    {
                        if (test.Name.StartsWith('<'))
                        {
                            await RunNestedTest(test);
                        }
                        else
                        {
                            await RunTest(test);
                        }
                        frameworkHandle.RecordEnd(testCase, TestOutcome.Passed);
                    }
                    catch
                    {
                        frameworkHandle.RecordEnd(testCase, TestOutcome.Failed);
                    }
                }
            }
        }

        private async ValueTask RunTest(MethodInfo test)
        {
            var assertionSetAttribute = test.DeclaringType.GetCustomAttribute<AssertionSetAttribute>();
            if (test.GetCustomAttribute<AssertionSetAttribute>() is not null)
            {
                assertionSetAttribute = test.GetCustomAttribute<AssertionSetAttribute>();
            }

            if (assertionSetAttribute is null)
            {
                if (test.DeclaringType.IsAbstract && test.DeclaringType.IsSealed)
                {
                    await InvokeTest(test, null, null);
                }
                else
                {
                    await InvokeTest(test, Activator.CreateInstance(test.DeclaringType), null);
                }

                return;
            }

            var assertionSetInstance = Activator.CreateInstance(assertionSetAttribute.AssertionSetType) as AssertionSet;
            assertionSetInstance!.Configure();
            assertionSetInstance.AddSingleton(test.DeclaringType);

            var provider = assertionSetInstance.BuildServiceProvider();
            var parameters = test.GetParameters().Select(s =>
            {
                var service = provider.GetService(s.ParameterType);
                if (service != null)
                {
                    return service;
                }

                if (s.ParameterType.IsInterface)
                {
                    var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(s.ParameterType))!;
                    return mock.Object;
                }

                return null;
            }).ToArray();

            var typeInstance = provider.GetRequiredService(test.DeclaringType);
            await InvokeTest(test, typeInstance, parameters);
        }

        private async ValueTask RunNestedTest(MethodInfo test)
        {
            var parentMethodName = test.Name.Split('>')[0].Substring(1);
            var parentMethod = test.DeclaringType.GetMethod(parentMethodName);

            var callback = Delegate.CreateDelegate(typeof(Action), test);



            await InvokeTest(parentMethod, Activator.CreateInstance(test.DeclaringType), new object[1] { callback });
        }

        private async ValueTask InvokeTest(MethodInfo methodInfo, object? typeInstance, object?[]? parameters)
        {
            var isAwaitable = methodInfo.ReturnType.GetMethod(nameof(Task.GetAwaiter)) != null;
            if (isAwaitable)
            {
                await (dynamic)methodInfo.Invoke(typeInstance, parameters)!;
            }
            else
            {
                methodInfo.Invoke(typeInstance, parameters);
            }
        }
    }
}
