using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMCP.Core
{
    /// <summary>
    /// Marshals work from bridge I/O threads onto Unity's main thread. Almost every
    /// Unity API must be touched from the main thread, so handlers run here while the
    /// requesting socket thread blocks for the result.
    /// </summary>
    public static class MainThreadDispatcher
    {
        static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        static bool _initialized;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            while (Queue.TryDequeue(out Action action))
            {
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>
        /// Enqueues <paramref name="func"/> for the main thread and blocks the caller
        /// until it completes (or the timeout elapses). Exceptions are rethrown on the
        /// calling thread so the bridge can translate them into an error response.
        /// </summary>
        public static T Run<T>(Func<T> func, int timeoutMs = 30000)
        {
            T result = default(T);
            Exception captured = null;

            using (var done = new ManualResetEventSlim(false))
            {
                Queue.Enqueue(() =>
                {
                    try { result = func(); }
                    catch (Exception e) { captured = e; }
                    finally { done.Set(); }
                });

                if (!done.Wait(timeoutMs))
                    throw new TimeoutException(
                        "Main-thread operation did not complete within " + timeoutMs + "ms. " +
                        "Is the Editor compiling, paused on a modal dialog, or stuck in a long import?");
            }

            if (captured != null) throw captured;
            return result;
        }
    }
}
