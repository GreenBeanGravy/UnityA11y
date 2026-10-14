using System;

namespace UnityA11y.Core
{
    /// <summary>
    /// Where the engine-neutral layers report problems. A11yRuntime points this at its own Log
    /// when it starts, so the host's logger receives everything.
    ///
    /// Nothing below the Unity layer may call UnityEngine.Debug: these layers also run on UIA
    /// RPC threads, and under IL2CPP a Unity call from a thread the runtime does not know about
    /// crashes the process.
    /// </summary>
    public static class A11yLog
    {
        public static Action<string> Sink = _ => { };

        public static void Write(string message)
        {
            try { Sink?.Invoke(message); }
            catch { /* a broken logger must never take the tree down with it */ }
        }
    }
}
