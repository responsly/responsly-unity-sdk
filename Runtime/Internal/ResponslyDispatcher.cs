using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Scripting;

namespace Responsly.Internal
{
    /// <summary>
    /// Runs work on Unity's main thread. The native SDKs report events on the platform's UI
    /// thread, which on Android is not Unity's main thread; everything is queued here and run from
    /// <c>Update</c> of a hidden object that survives scene loads. The object is created the first
    /// time it is needed and never in the Editor, where the SDK does nothing.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class ResponslyDispatcher : MonoBehaviour
    {
        private static readonly object s_lock = new object();
        private static readonly Queue<Action> s_queue = new Queue<Action>();
        private static readonly SendOrPostCallback s_createOnMainThread = CreateOnMainThread;

        private static ResponslyDispatcher s_instance;
        private static SynchronizationContext s_mainContext;
        private static volatile int s_mainThreadId;
        private static volatile bool s_hasInstance;
        private static volatile bool s_sceneLoadReached;
        private static volatile bool s_createRequested;
        private static volatile bool s_quitting;

        /// <summary>True on Unity's main thread (and before Unity told us which thread that is).</summary>
        public static bool IsMainThread
        {
            get
            {
                int mainThreadId = s_mainThreadId;
                return mainThreadId == 0 || Thread.CurrentThread.ManagedThreadId == mainThreadId;
            }
        }

        /// <summary>Runs the action now when called on the main thread, otherwise on the next frame.</summary>
        public static void Run(Action action)
        {
            if (IsMainThread)
            {
                // The first SDK call creates the object, so that events can be delivered later
                // without creating anything from inside a native callback.
                RequestInstance();
                RunSafely(action);
            }
            else
            {
                Post(action);
            }
        }

        /// <summary>Runs the action on the main thread on the next frame. Safe from any thread.</summary>
        public static void Post(Action action)
        {
            lock (s_lock)
            {
                s_queue.Enqueue(action);
            }
            RequestInstance();
        }

        // The first callback Unity makes, always on the main thread.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnSubsystemRegistration()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_instance = null;
            s_hasInstance = false;
            s_sceneLoadReached = false;
            s_quitting = false;
        }

        // From here on game objects can be created.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_mainContext = SynchronizationContext.Current;
            s_sceneLoadReached = true;

            bool needed = s_createRequested;
            lock (s_lock)
            {
                needed = needed || s_queue.Count > 0;
            }
#if !UNITY_EDITOR
            // Without a synchronization context a background thread could not ask for the object
            // later, so it is created up front.
            needed = needed || s_mainContext == null;
#endif
            if (needed)
            {
                CreateInstance();
            }
        }

        private static void RequestInstance()
        {
            if (s_hasInstance)
            {
                return;
            }
            if (IsMainThread)
            {
                if (s_sceneLoadReached)
                {
                    CreateInstance();
                }
                else
                {
                    s_createRequested = true;
                }
                return;
            }

            SynchronizationContext context = s_mainContext;
            if (context != null)
            {
                context.Post(s_createOnMainThread, null);
            }
            else
            {
                s_createRequested = true;
            }
        }

        private static void CreateOnMainThread(object state)
        {
            CreateInstance();
        }

        // Main thread only.
        private static void CreateInstance()
        {
            if (s_hasInstance || s_quitting)
            {
                return;
            }
            s_createRequested = false;
            GameObject host = new GameObject("Responsly");
            host.hideFlags = HideFlags.HideInHierarchy;
            UnityEngine.Object.DontDestroyOnLoad(host);
            s_instance = host.AddComponent<ResponslyDispatcher>();
            s_hasInstance = true;
        }

        private static void RunSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [Preserve]
        private void Update()
        {
            int pending;
            lock (s_lock)
            {
                pending = s_queue.Count;
            }

            // Work queued while this runs waits for the next frame.
            while (pending > 0)
            {
                pending--;
                Action action;
                lock (s_lock)
                {
                    if (s_queue.Count == 0)
                    {
                        return;
                    }
                    action = s_queue.Dequeue();
                }
                RunSafely(action);
            }
        }

        [Preserve]
        private void OnDestroy()
        {
            if (ReferenceEquals(s_instance, this))
            {
                s_instance = null;
                s_hasInstance = false;
            }
        }

        [Preserve]
        private void OnApplicationQuit()
        {
            s_quitting = true;
        }
    }
}
