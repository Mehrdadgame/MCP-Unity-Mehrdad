using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityMCP.Protocol;

namespace UnityMCP.Core
{
    /// <summary>
    /// Runs a list of operations inside a single Undo group (so the whole batch reverts with
    /// one Ctrl+Z). With atomic=true, the first failure reverts everything done so far.
    /// Must run on the main thread.
    /// </summary>
    public static class BatchExecutor
    {
        public static object Execute(JObject batch)
        {
            string undoGroup = (string)batch["undoGroup"] ?? "MCP Batch";
            bool atomic = batch["atomic"] != null && (bool)batch["atomic"];
            var ops = batch["ops"] as JArray ?? new JArray();

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoGroup);
            int group = Undo.GetCurrentGroup();

            var results = new List<object>();
            int failedIndex = -1;

            for (int i = 0; i < ops.Count; i++)
            {
                var op = ops[i] as JObject;
                if (op == null)
                {
                    results.Add(new { success = false, error = new { code = ErrorCodes.MALFORMED_REQUEST, message = "Op is not an object." } });
                    if (atomic) { failedIndex = i; Undo.RevertAllDownToGroup(group); break; }
                    continue;
                }

                try
                {
                    var req = new Request
                    {
                        category = (string)op["category"],
                        action = (string)op["action"],
                        @params = op["params"] as JObject ?? new JObject(),
                    };
                    var data = CommandRouter.Route(req);
                    results.Add(new { success = true, data = data });
                }
                catch (Exception e)
                {
                    string code = e is HandlerException he ? he.Code : ErrorCodes.EXCEPTION;
                    results.Add(new { success = false, error = new { code = code, message = e.Message } });
                    if (atomic) { failedIndex = i; Undo.RevertAllDownToGroup(group); break; }
                }
            }

            Undo.CollapseUndoOperations(group);
            return new
            {
                success = failedIndex < 0,
                count = results.Count,
                failedIndex = failedIndex, // -1 when nothing failed (always present)
                undoGroup = undoGroup,
                results = results,
            };
        }
    }
}
