using System;
using System.Collections.Generic;

namespace UnityMCP.Core
{
    /// <summary>Thread-safe ring buffer of recent bridge requests, shown in the MCP Control Panel.</summary>
    public static class RequestLog
    {
        public struct Entry
        {
            public string time;
            public string label;
            public bool success;
            public int ms;
        }

        const int Max = 200;
        static readonly List<Entry> Entries = new List<Entry>();
        static int _total;

        public static int Total { get { return _total; } }

        public static void Record(string label, bool success, int ms)
        {
            lock (Entries)
            {
                _total++;
                Entries.Add(new Entry { time = DateTime.Now.ToString("HH:mm:ss"), label = label, success = success, ms = ms });
                if (Entries.Count > Max) Entries.RemoveAt(0);
            }
        }

        public static List<Entry> Snapshot()
        {
            lock (Entries) { return new List<Entry>(Entries); }
        }

        public static void Clear()
        {
            lock (Entries) { Entries.Clear(); }
        }
    }
}
