using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace R3DUnison.Core
{
    /// <summary>
    /// Marshals work from network/background threads onto the Unity main thread.
    /// Unity APIs are main-thread-only; every socket callback must go through Post().
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        private static MainThreadDispatcher _instance;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("R3DUnison.MainThreadDispatcher");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<MainThreadDispatcher>();
        }

        public static void Post(Action action) => Queue.Enqueue(action);

        /// <summary>Per-frame tick on the main thread; transports poll their sockets here.</summary>
        public static event Action OnFrame;

        private void Update()
        {
            while (Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Main.LogError($"Dispatched action threw: {e}");
                }
            }
            try
            {
                OnFrame?.Invoke();
            }
            catch (Exception e)
            {
                Main.LogError($"OnFrame handler threw: {e}");
            }
        }
    }
}
