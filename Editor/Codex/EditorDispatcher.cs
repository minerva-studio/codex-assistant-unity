using System;
using System.Collections.Concurrent;
using UnityEditor;

namespace Minerva.Codex
{
    /// <summary>
    /// Runs callbacks on the Unity main thread. App-server reads happen on a
    /// background thread; everything that touches Unity state goes through here.
    /// </summary>
    internal static class EditorDispatcher
    {
        static readonly ConcurrentQueue<Action> s_Queue = new ConcurrentQueue<Action>();
        static bool s_Hooked;

        [InitializeOnLoadMethod]
        static void Hook()
        {
            if (s_Hooked)
            {
                return;
            }

            s_Hooked = true;
            EditorApplication.update += Pump;
        }

        public static void Enqueue(Action action)
        {
            if (action != null)
            {
                s_Queue.Enqueue(action);
            }
        }

        public static void Pump()
        {
            while (s_Queue.TryDequeue(out Action action))
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogException(exception);
                }
            }
        }
    }
}
