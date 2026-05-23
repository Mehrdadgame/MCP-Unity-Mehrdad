using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityMCP.Protocol;

namespace UnityMCP.Utils
{
    /// <summary>
    /// Reads the Unity Editor Console through the internal UnityEditor.LogEntries API
    /// (via reflection). Captures everything the Console shows — compile errors, asset
    /// import errors, Debug.Log/Warning/Error — and persists across domain reloads.
    /// </summary>
    public static class ConsoleLogReader
    {
        static Type _tEntries, _tEntry;
        static MethodInfo _start, _end, _count, _getEntry, _clear;
        static FieldInfo _fMessage, _fMode, _fFile, _fLine;
        static bool _init;

        // UnityEditor internal "Mode" flag groups (stable across recent Unity versions).
        const int ErrorMask =
            (1 << 0)  /* Error */             | (1 << 1)  /* Assert */ |
            (1 << 4)  /* Fatal */             | (1 << 6)  /* AssetImportError */ |
            (1 << 8)  /* ScriptingError */    | (1 << 11) /* ScriptCompileError */ |
            (1 << 16) /* ScriptingAssertion */| (1 << 17) /* ScriptingException */;
        const int WarningMask =
            (1 << 7)  /* AssetImportWarning */ | (1 << 9)  /* ScriptingWarning */ |
            (1 << 12) /* ScriptCompileWarning */;

        static void EnsureInit()
        {
            if (_init) return;
            Assembly editorAsm = typeof(EditorApplication).Assembly;
            _tEntries = editorAsm.GetType("UnityEditor.LogEntries");
            _tEntry = editorAsm.GetType("UnityEditor.LogEntry");
            if (_tEntries == null || _tEntry == null)
                throw new HandlerException(ErrorCodes.EXCEPTION,
                    "UnityEditor.LogEntries API is unavailable in this Unity version.");

            const BindingFlags M = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            _start = _tEntries.GetMethod("StartGettingEntries", M);
            _end = _tEntries.GetMethod("EndGettingEntries", M);
            _count = _tEntries.GetMethod("GetCount", M);
            _getEntry = _tEntries.GetMethod("GetEntryInternal", M) ?? _tEntries.GetMethod("GetEntry", M);
            _clear = _tEntries.GetMethod("Clear", M);

            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _fMessage = _tEntry.GetField("message", F) ?? _tEntry.GetField("condition", F);
            _fMode = _tEntry.GetField("mode", F);
            _fFile = _tEntry.GetField("file", F);
            _fLine = _tEntry.GetField("line", F);

            if (_start == null || _end == null || _count == null || _getEntry == null || _fMode == null)
                throw new HandlerException(ErrorCodes.EXCEPTION,
                    "Console reflection members not found in this Unity version.");
            _init = true;
        }

        static string Classify(int mode)
        {
            if ((mode & ErrorMask) != 0) return "Error";
            if ((mode & WarningMask) != 0) return "Warning";
            return "Log";
        }

        public static object GetLogs(string level, int limit)
        {
            EnsureInit();
            if (limit <= 0) limit = 100;
            bool all = string.IsNullOrEmpty(level) || level.Equals("All", StringComparison.OrdinalIgnoreCase);

            int errorCount = 0, warningCount = 0, total = 0;
            var collected = new List<object>();

            _start.Invoke(null, null);
            try
            {
                total = (int)_count.Invoke(null, null);
                object entry = Activator.CreateInstance(_tEntry);
                var args = new object[2];

                for (int i = 0; i < total; i++)
                {
                    args[0] = i; args[1] = entry;
                    _getEntry.Invoke(null, args);

                    int mode = Convert.ToInt32(_fMode.GetValue(entry));
                    string lvl = Classify(mode);
                    if (lvl == "Error") errorCount++;
                    else if (lvl == "Warning") warningCount++;

                    if (!all && !lvl.Equals(level, StringComparison.OrdinalIgnoreCase)) continue;

                    collected.Add(new
                    {
                        level = lvl,
                        message = _fMessage != null ? _fMessage.GetValue(entry) as string : null,
                        file = _fFile != null ? _fFile.GetValue(entry) as string : null,
                        line = _fLine != null ? Convert.ToInt32(_fLine.GetValue(entry)) : 0,
                    });
                }
            }
            finally
            {
                _end.Invoke(null, null);
            }

            // Keep only the most recent `limit` matching entries.
            int startIdx = Math.Max(0, collected.Count - limit);
            var entries = collected.GetRange(startIdx, collected.Count - startIdx);

            return new
            {
                total = total,
                errorCount = errorCount,
                warningCount = warningCount,
                shown = entries.Count,
                entries = entries,
            };
        }

        public static void Clear()
        {
            EnsureInit();
            if (_clear != null) _clear.Invoke(null, null);
        }
    }
}
