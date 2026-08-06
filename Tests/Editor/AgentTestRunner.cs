using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace Hlight.Audio.Tests
{
    /// Runs this assembly's EditMode tests synchronously and writes the result to a file.
    /// Exists because agents cannot drive the Test Runner window, and TestRunnerApi is
    /// async and occasionally never fires its callback. Supports [SetUp]/[Test]/[TearDown]
    /// only — no [TestCase], [OneTimeSetUp] or [UnityTest]. Humans use the window as usual.
    public static class AgentTestRunner
    {
        private const string RESULT_PATH = "Temp/audio-tests.txt";

        [MenuItem("Tools/Hlight/Run Audio Tests")]
        public static void Run()
        {
            var report = new StringBuilder();
            var passed = 0;
            var failed = 0;

            foreach (var type in typeof(AgentTestRunner).Assembly.GetTypes())
            {
                var tests = type.GetMethods().Where(m => Has(m, "TestAttribute")).ToArray();
                if (tests.Length == 0) continue;

                var setUps = type.GetMethods().Where(m => Has(m, "SetUpAttribute")).ToArray();
                var tearDowns = type.GetMethods().Where(m => Has(m, "TearDownAttribute")).ToArray();

                foreach (var test in tests)
                {
                    object fixture = null;
                    try
                    {
                        fixture = Activator.CreateInstance(type);
                        foreach (var setUp in setUps) setUp.Invoke(fixture, null);
                        test.Invoke(fixture, null);
                        passed++;
                        report.Append("Passed ").Append(type.Name).Append('.').Append(test.Name).Append('\n');
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        var actual = exception is TargetInvocationException ? exception.InnerException : exception;
                        report.Append("Failed ").Append(type.Name).Append('.').Append(test.Name).Append('\n')
                              .Append("  ").Append(actual.Message.Replace("\n", "\n  ")).Append('\n');
                    }
                    finally
                    {
                        foreach (var tearDown in tearDowns)
                        {
                            try { tearDown.Invoke(fixture, null); }
                            catch (Exception exception) { report.Append("  teardown: ").Append(exception.Message).Append('\n'); }
                        }
                    }
                }
            }

            var summary = $"PASS={passed} FAIL={failed}\n";
            File.WriteAllText(RESULT_PATH, summary + report);
            UnityEngine.Debug.Log("[AgentTestRunner] " + summary.Trim());
        }

        private static bool Has(MethodInfo method, string attributeName)
        {
            return method.GetCustomAttributes().Any(a => a.GetType().Name == attributeName);
        }
    }
}
