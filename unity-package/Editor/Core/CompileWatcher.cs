using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;

namespace UnityMCP.Core
{
    /// <summary>
    /// Captures C# compiler messages straight from the CompilationPipeline and stores
    /// them in SessionState, so the result of a compile survives the domain reload that
    /// the compile itself triggers and can be read back after the bridge reconnects.
    /// </summary>
    [InitializeOnLoad]
    public static class CompileWatcher
    {
        const string KeyMessages = "MCP.Compile.Messages";
        const string KeyFinishedTicks = "MCP.Compile.FinishedTicks";
        const string KeySucceeded = "MCP.Compile.Succeeded";

        static readonly List<CompileMessage> Buffer = new List<CompileMessage>();

        static CompileWatcher()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;
        }

        static void OnCompilationStarted(object context)
        {
            Buffer.Clear();
            SessionState.SetString(KeyMessages, "[]");
        }

        static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null) return;
            foreach (var m in messages)
            {
                Buffer.Add(new CompileMessage
                {
                    type = m.type.ToString(),
                    message = m.message,
                    file = m.file,
                    line = m.line,
                    column = m.column,
                    assembly = Path.GetFileNameWithoutExtension(assemblyPath),
                });
            }
            SessionState.SetString(KeyMessages, JsonConvert.SerializeObject(Buffer));
        }

        static void OnCompilationFinished(object context)
        {
            bool hasError = Buffer.Exists(m => string.Equals(m.type, "Error", StringComparison.OrdinalIgnoreCase));
            SessionState.SetBool(KeySucceeded, !hasError);
            SessionState.SetString(KeyFinishedTicks, DateTime.UtcNow.Ticks.ToString());
        }

        public static long LastFinishedTicks
        {
            get
            {
                long ticks;
                long.TryParse(SessionState.GetString(KeyFinishedTicks, "0"), out ticks);
                return ticks;
            }
        }

        public static object GetResult()
        {
            List<CompileMessage> msgs;
            try { msgs = JsonConvert.DeserializeObject<List<CompileMessage>>(SessionState.GetString(KeyMessages, "[]")); }
            catch { msgs = null; }
            if (msgs == null) msgs = new List<CompileMessage>();

            var errors = msgs.FindAll(m => string.Equals(m.type, "Error", StringComparison.OrdinalIgnoreCase));
            var warnings = msgs.FindAll(m => string.Equals(m.type, "Warning", StringComparison.OrdinalIgnoreCase));

            return new
            {
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                succeeded = SessionState.GetBool(KeySucceeded, true),
                errorCount = errors.Count,
                warningCount = warnings.Count,
                errors = errors,
                warnings = warnings,
                finishedAtTicks = LastFinishedTicks,
            };
        }
    }

    public class CompileMessage
    {
        public string type;
        public string message;
        public string file;
        public int line;
        public int column;
        public string assembly;
    }
}
