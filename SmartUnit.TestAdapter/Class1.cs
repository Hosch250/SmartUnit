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

namespace SmartUnit.TestAdapter
{
    [FileExtension(".dll")]
    [FileExtension(".exe")]
    [DefaultExecutorUri(TestExecutor.ExecutorUri)]
    public class TestDiscoverer : ITestDiscoverer
    {
        public void DiscoverTests(IEnumerable<string> sources, IDiscoveryContext discoveryContext, IMessageLogger logger, ITestCaseDiscoverySink discoverySink)
        {
            foreach (var source in sources)
            {
                var sourceAssemblyPath = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
                logger.SendMessage(TestMessageLevel.Informational, $"Discovered source '{source}'");

                var assembly = Assembly.LoadFrom(sourceAssemblyPath);

                var tests = assembly.GetTypes().SelectMany(s => s.GetMethods().Where(w => w.GetCustomAttribute<AssertionAttribute>() != null));
                foreach (var test in tests)
                {
                    logger.SendMessage(TestMessageLevel.Informational, $"Discovered test '{test.DeclaringType.FullName + "." + test.Name}'");

                    discoverySink.SendTestCase(new TestCase()
                    {
                        FullyQualifiedName = test.DeclaringType.FullName + "." + test.Name,
                        DisplayName = test.Name,
                        Source = source
                    });
                }
            }
        }
    }

    [ExtensionUri(ExecutorUri)]
    public class TestExecutor : ITestExecutor
    {
        public const string ExecutorUri = "executor://SmartUnitExecutor";

        private CancellationTokenSource cancellationToken;

        public void Cancel()
        {
            cancellationToken.Cancel();
        }

        public void RunTests(IEnumerable<TestCase> tests, IRunContext runContext, IFrameworkHandle frameworkHandle)
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
                    RunTest(testMethod);
                    frameworkHandle.RecordEnd(testCase, TestOutcome.Passed);
                }
                catch
                {
                    frameworkHandle.RecordEnd(testCase, TestOutcome.Failed);
                }
            }
        }

        public void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            foreach (var source in sources)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var sourceAssemblyPath = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);
                var assembly = Assembly.LoadFrom(sourceAssemblyPath);

                var tests = assembly.GetTypes().SelectMany(s => s.GetMethods().Where(w => w.GetCustomAttribute<AssertionAttribute>() != null));
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
                        RunTest(test);
                        frameworkHandle.RecordEnd(testCase, TestOutcome.Passed);
                    }
                    catch
                    {
                        frameworkHandle.RecordEnd(testCase, TestOutcome.Failed);
                    }
                }
            }
        }

        private void RunTest(MethodInfo test)
        {
            var typeInstance = Activator.CreateInstance(test.DeclaringType);

            if (!test.GetParameters().Any())
            {
                test.Invoke(typeInstance, null);
            }
            else
            {
                var assertionSetAttribute = test.DeclaringType.GetCustomAttribute<AssertionSetAttribute>();
                var assertionSetInstance = Activator.CreateInstance(assertionSetAttribute.AssertionSetType) as AssertionSet;
                assertionSetInstance.Configure();

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
                        var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(s.ParameterType));
                        return mock.Object;
                    }

                    return null;
                }).ToArray();
                test.Invoke(typeInstance, parameters);
            }
        }
    }
}
