using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
            foreach (var testCase in DiscoverTestCases(sources))
            {
                discoverySink.SendTestCase(testCase);
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

                await RunTestCase(testCase, frameworkHandle);
            }
        }

        public async void RunTests(IEnumerable<string> sources, IRunContext runContext, IFrameworkHandle frameworkHandle)
        {
            foreach (var testCase in DiscoverTestCases(sources))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await RunTestCase(testCase, frameworkHandle);
            }
        }

        private IEnumerable<TestCase> DiscoverTestCases(IEnumerable<string> sources)
        {
            foreach (var source in sources)
            {
                var sourceAssemblyPath = Path.IsPathRooted(source) ? source : Path.Combine(Directory.GetCurrentDirectory(), source);

                var assembly = Assembly.LoadFrom(sourceAssemblyPath);
                var tests = assembly.GetTypes()
                    .SelectMany(s => s.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    .Where(w => w.GetCustomAttribute<AssertionAttribute>() is not null)
                    .ToList();

                foreach (var test in tests)
                {
                    var testDisplayName = test.Name;
                    if (test.Name.StartsWith('<'))
                    {
                        var parentTestName = test.Name.Split('>')[0][1..];
                        testDisplayName = parentTestName + '.' + test.Name.Split('>')[1][3..].Split('|')[0];
                    }

                    var assertionAttribute = test.GetCustomAttribute<AssertionAttribute>()!;
                    var testCase = new TestCase(test.DeclaringType.FullName + "." + test.Name, new Uri(ExecutorUri), source)
                    {
                        DisplayName = string.IsNullOrEmpty(assertionAttribute.Name) ? testDisplayName : assertionAttribute.Name,
                    };

                    yield return testCase;
                }
            }
        }

        private MethodInfo GetTestMethodFromCase(TestCase testCase)
        {
            var sourceAssemblyPath = Path.IsPathRooted(testCase.Source) ? testCase.Source : Path.Combine(Directory.GetCurrentDirectory(), testCase.Source);
            var assembly = Assembly.LoadFrom(sourceAssemblyPath);

            var fullyQualifiedName = testCase.FullyQualifiedName;
            var nameSeparatorIndex = fullyQualifiedName.LastIndexOf('.');
            var typeName = fullyQualifiedName.Substring(0, nameSeparatorIndex);

            var testClass = assembly.GetType(typeName);
            return testClass.GetMethod(fullyQualifiedName.Substring(nameSeparatorIndex + 1), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
        }

        private void RecordSkippedTest(TestCase testCase, string? reason, ITestExecutionRecorder recorder)
        {
            var now = DateTime.Now;
            var testResult = new TestResult(testCase)
            {
                Outcome = TestOutcome.Skipped,
                StartTime = now,
                EndTime = now,
                Duration = new TimeSpan(),
                DisplayName = testCase.DisplayName,
                ErrorMessage = reason
            };

            recorder.RecordResult(testResult);
        }

        private void RecordPassedTest(TestCase testCase, DateTime start, DateTime end, ITestExecutionRecorder recorder)
        {
            var testResult = new TestResult(testCase)
            {
                Outcome = TestOutcome.Passed,
                StartTime = start,
                EndTime = end,
                Duration = end - start,
                DisplayName = testCase.DisplayName
            };

            recorder.RecordResult(testResult);
        }

        private void RecordFailedTest(TestCase testCase, DateTime start, DateTime end, Exception ex, ITestExecutionRecorder recorder)
        {
            var testResult = new TestResult(testCase)
            {
                Outcome = TestOutcome.Failed,
                StartTime = start,
                EndTime = end,
                Duration = end - start,
                DisplayName = testCase.DisplayName,
                ErrorMessage = ex.Message,
                ErrorStackTrace = ex.StackTrace
            };

            recorder.RecordResult(testResult);
        }

        private async ValueTask RunTestCase(TestCase testCase, ITestExecutionRecorder recorder)
        {
            var testMethod = GetTestMethodFromCase(testCase);
            if (testMethod.GetCustomAttribute<SkipAttribute>() is not null)
            {
                RecordSkippedTest(testCase, testMethod.GetCustomAttribute<SkipAttribute>()!.Reason, recorder);
                return;
            }

            recorder.RecordStart(testCase);
            var start = DateTime.Now;

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

                var end = DateTime.Now;
                RecordPassedTest(testCase, start, end, recorder);
            }
            catch (Exception ex)
            {
                var end = DateTime.Now;
                RecordFailedTest(testCase, start, end, ex.InnerException ?? ex, recorder);
            }
        }

        private async ValueTask RunNestedTest(MethodInfo test)
        {
            var parentMethodName = test.Name.Split('>')[0].Substring(1);
            var parentMethod = test.DeclaringType.GetMethod(parentMethodName);

            await RunTest(parentMethod, test);
        }

        private async ValueTask RunTest(MethodInfo test, MethodInfo? callback = null)
        {
            var assertionSetAttribute = test.DeclaringType.GetCustomAttribute<AssertionSetAttribute>();
            if (test.GetCustomAttribute<AssertionSetAttribute>() is not null)
            {
                assertionSetAttribute = test.GetCustomAttribute<AssertionSetAttribute>();
            }

            if (assertionSetAttribute is null)
            {
                await RunTestWithoutAssertionSet(test, callback);
            }
            else
            {
                await RunTestWithAssertionSet(test, callback, assertionSetAttribute.AssertionSetType);
            }
        }

        private async ValueTask RunTestWithoutAssertionSet(MethodInfo test, MethodInfo? callback)
        {
            var parameters = test.GetParameters().Select(s =>
            {
                if (s.GetCustomAttribute<CallbackAttribute>() is not null && callback is not null)
                {
                    return Delegate.CreateDelegate(s.ParameterType, callback);
                }

                if (s.ParameterType.IsInterface)
                {
                    var mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(s.ParameterType))!;
                    return mock.Object;
                }

                return null;
            }).ToArray();

            var typeInstance = test.DeclaringType.IsAbstract && test.DeclaringType.IsSealed ? null : Activator.CreateInstance(test.DeclaringType);
            if (test.DeclaringType.IsAbstract && test.DeclaringType.IsSealed)
            {
                await InvokeTest(test, typeInstance, parameters);
            }
            else
            {
                await InvokeTest(test, typeInstance, parameters);
            }
        }

        private async ValueTask RunTestWithAssertionSet(MethodInfo test, MethodInfo? callback, Type assertionSetType)
        {
            var assertionSetInstance = Activator.CreateInstance(assertionSetType) as AssertionSet;
            assertionSetInstance!.Configure();
            assertionSetInstance.AddSingleton(test.DeclaringType);

            var provider = assertionSetInstance.BuildServiceProvider();
            var parameters = test.GetParameters().Select(s =>
            {
                if (s.GetCustomAttribute<CallbackAttribute>() is not null && callback is not null)
                {
                    return Delegate.CreateDelegate(s.ParameterType, callback);
                }

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

            var typeInstance = test.DeclaringType.IsAbstract && test.DeclaringType.IsSealed ? null : provider.GetRequiredService(test.DeclaringType);
            await InvokeTest(test, typeInstance, parameters);
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
