using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if IL2CPP
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif

namespace UnityA11y.Unity
{
    /// <summary>
    /// Everything that differs between a Mono game and an IL2CPP game, in one place.
    ///
    /// Under IL2CPP every Unity object is a generated wrapper whose C# type is whatever the
    /// call site asked for, not what the object really is. A Toggle fetched as a Selectable is a
    /// Selectable wrapper, so <c>sel as Toggle</c> is null and <c>sel is Button</c> is false, and
    /// nothing reports an error: every control just turns into a plain button. The only reliable
    /// test is <c>TryCast</c>, which asks the IL2CPP runtime. Build with IL2CPP defined for those
    /// games (UnityA11y.Il2Cpp.csproj, or compile these sources into the mod with the define).
    /// </summary>
    public static class Compat
    {
#if IL2CPP
        /// <summary>The object as a T, or null. Never use <c>as</c> on a Unity object here.</summary>
        public static T As<T>(object o) where T : Il2CppObjectBase
        {
            var wrapper = o as Il2CppObjectBase;
            return wrapper != null ? wrapper.TryCast<T>() : null;
        }

        public static bool Is<T>(object o) where T : Il2CppObjectBase => As<T>(o) != null;
#else
        /// <summary>The object as a T, or null.</summary>
        public static T As<T>(object o) where T : class => o as T;

        public static bool Is<T>(object o) where T : class => o is T;
#endif

        /// <summary>Both corners Unity reports, bottom-left [0] and top-right [2], in world space.</summary>
        public static void GetWorldCorners(RectTransform rt, out Vector3 bottomLeft, out Vector3 topRight)
        {
#if IL2CPP
            // The interop signature takes an Il2CppStructArray. Passing a Vector3[] converts it
            // into a fresh copy, so the caller's array is never filled and every bound is zero.
            var corners = new Il2CppStructArray<Vector3>(4);
#else
            var corners = new Vector3[4];
#endif
            rt.GetWorldCorners(corners);
            bottomLeft = corners[0];
            topRight = corners[2];
        }

        // ---- Selectable.allSelectables ---------------------------------------------------

        private static readonly List<Selectable> _selectables = new List<Selectable>(256);
#if IL2CPP
        private static Il2CppReferenceArray<Selectable> _selectableBuffer;
#else
        private static bool _probed;
        private static System.Reflection.PropertyInfo _arrayProp;
        private static System.Reflection.PropertyInfo _listProp;
#endif

        /// <summary>
        /// Every enabled Selectable, into a shared list that the next call overwrites.
        /// </summary>
        public static List<Selectable> AllSelectables()
        {
            _selectables.Clear();
#if IL2CPP
            // allSelectablesArray returns an Il2CppReferenceArray, and allSelectables an
            // Il2CppSystem List that implements no System interface, so the Mono path below
            // silently finds nothing. Fill one reused native buffer instead of allocating.
            int count = Selectable.allSelectableCount;
            if (_selectableBuffer == null || _selectableBuffer.Length < count)
                _selectableBuffer = new Il2CppReferenceArray<Selectable>(Math.Max(64, count * 2));
            int filled = Selectable.AllSelectablesNoAlloc(_selectableBuffer);
            for (int i = 0; i < filled; i++) _selectables.Add(_selectableBuffer[i]);
#else
            // Selectable.allSelectablesArray only exists from Unity 2019.3. Older games expose the
            // List-based allSelectables instead, and calling the missing one throws
            // MissingMethodException at JIT time, which is not catchable at the call site in a
            // useful way. So resolve it once by reflection.
            if (!_probed)
            {
                _probed = true;
                var t = typeof(Selectable);
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                _arrayProp = t.GetProperty("allSelectablesArray", flags);
                _listProp = t.GetProperty("allSelectables", flags);
            }

            if (_arrayProp != null)
            {
                var arr = _arrayProp.GetValue(null, null) as Selectable[];
                if (arr != null) _selectables.AddRange(arr);
            }
            else if (_listProp != null)
            {
                var list = _listProp.GetValue(null, null) as IList<Selectable>;
                if (list != null)
                    for (int i = 0; i < list.Count; i++) _selectables.Add(list[i]);
            }
#endif
            return _selectables;
        }
    }

    /// <summary>
    /// Labels and descriptions supplied by the host for specific GameObjects, overriding what
    /// the scanner would guess. Works in every build; the <see cref="A11yLabel"/> component does
    /// the same on Mono only, since IL2CPP cannot add an unregistered MonoBehaviour.
    /// </summary>
    public static class A11yLabels
    {
        private struct Entry { public string Label; public string Description; }

        private static readonly Dictionary<int, Entry> _byInstance = new Dictionary<int, Entry>();

        public static void Set(GameObject go, string label, string description = null)
        {
            if (go == null) return;
            _byInstance[go.GetInstanceID()] = new Entry { Label = label, Description = description };
        }

        public static void Clear(GameObject go)
        {
            if (go != null) _byInstance.Remove(go.GetInstanceID());
        }

        public static void ClearAll() => _byInstance.Clear();

        public static bool TryGet(GameObject go, out string label, out string description)
        {
            Entry e;
            if (go != null && _byInstance.TryGetValue(go.GetInstanceID(), out e))
            {
                label = e.Label;
                description = e.Description;
                return true;
            }
#if !IL2CPP
            var component = go != null ? go.GetComponent<A11yLabel>() : null;
            if (component != null)
            {
                label = component.Label;
                description = component.Description;
                return true;
            }
#endif
            label = null;
            description = null;
            return false;
        }
    }
}
