using Newtonsoft.Json.Linq;
using UnityMCP.Handlers;
using UnityMCP.Protocol;
#if MCP_TESTFRAMEWORK
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEditor.TestTools.TestRunner.Api;
#endif

namespace UnityMCP.Handlers.V1
{
    /// <summary>Runs Edit/PlayMode tests via TestRunnerApi. Gated on com.unity.test-framework.</summary>
    public class TestHandler : HandlerBase
    {
#if !MCP_TESTFRAMEWORK
        public override object Handle(string action, JObject p)
        {
            throw new HandlerException(ErrorCodes.PACKAGE_ERROR,
                "The Test Framework (com.unity.test-framework) is not installed. Install it via unity_add_package.");
        }
#else
        static TestRunnerApi _api;
        static readonly Collector _collector = new Collector();

        public override object Handle(string action, JObject p)
        {
            switch (action)
            {
                case "run_editmode_tests": return Run(TestMode.EditMode, p);
                case "run_playmode_tests": return Run(TestMode.PlayMode, p);
                case "get_test_results": return Results();
                case "list_tests": return ListTests(p);
                default: throw UnknownAction(action);
            }
        }

        static TestRunnerApi Api()
        {
            if (_api == null) _api = ScriptableObject.CreateInstance<TestRunnerApi>();
            return _api;
        }

        static object Run(TestMode mode, JObject p)
        {
            var api = Api();
            api.RegisterCallbacks(_collector);
            SessionState.SetString("MCP.Test.Result", "");
            SessionState.SetBool("MCP.Test.Running", true);

            var filter = new Filter { testMode = mode };
            string nameFilter = OptString(p, "filter", null);
            if (!string.IsNullOrEmpty(nameFilter)) filter.testNames = new[] { nameFilter };
            api.Execute(new ExecutionSettings(filter));
            return new { started = true, mode = mode.ToString(), note = "Poll test.get_test_results until running=false." };
        }

        static object Results()
        {
            string res = SessionState.GetString("MCP.Test.Result", "");
            return new
            {
                running = SessionState.GetBool("MCP.Test.Running", false),
                result = string.IsNullOrEmpty(res) ? null : JToken.Parse(res),
            };
        }

        static object ListTests(JObject p)
        {
            var mode = OptString(p, "mode", "EditMode").ToLowerInvariant() == "playmode" ? TestMode.PlayMode : TestMode.EditMode;
            Api().RetrieveTestList(mode, root =>
            {
                var names = new List<string>();
                Flatten(root, names);
                SessionState.SetString("MCP.Test.List", JsonConvert.SerializeObject(names));
            });
            string cached = SessionState.GetString("MCP.Test.List", "");
            return new
            {
                requested = true,
                note = "Retrieval is async; call again to read the populated list.",
                tests = string.IsNullOrEmpty(cached) ? null : JToken.Parse(cached),
            };
        }

        static void Flatten(ITestAdaptor t, List<string> names)
        {
            if (t == null) return;
            if (!t.HasChildren && !t.IsTestAssembly) names.Add(t.FullName);
            if (t.Children != null) foreach (var c in t.Children) Flatten(c, names);
        }

        class Collector : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetBool("MCP.Test.Running", false);
                SessionState.SetString("MCP.Test.Result", JsonConvert.SerializeObject(new
                {
                    status = result.TestStatus.ToString(),
                    passed = result.PassCount,
                    failed = result.FailCount,
                    skipped = result.SkipCount,
                    inconclusive = result.InconclusiveCount,
                    durationSeconds = result.Duration,
                }));
            }
        }
#endif
    }
}
