using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityA11y.Core;

namespace UnityA11y.Unity
{
    /// <summary>
    /// Provider-mode focus for games whose own navigation moves the uGUI selection: the focused
    /// node is whatever <c>EventSystem.current.currentSelectedGameObject</c> is, so the game (or
    /// the mod) keeps every key it already handles and this library only reports the result.
    ///
    /// Node ids follow <see cref="UGuiSource.StableId"/>, so any source built on UGuiSource lines
    /// up without extra wiring. A screen reader's "move focus here" request selects that object
    /// in the EventSystem, resolved through the active source.
    /// </summary>
    public sealed class EventSystemFocusOwner : IFocusOwner
    {
        private readonly Func<ulong, GameObject> _resolve;
        private IUiSource _source;
        private ulong _last;

        /// <param name="resolve">
        /// Optional: maps a node id back to the GameObject to select, for sources that are not
        /// built on <see cref="UGuiSource"/>. When null, UGuiSource.Resolve is used.
        /// </param>
        public EventSystemFocusOwner(Func<ulong, GameObject> resolve = null)
        {
            _resolve = resolve;
        }

        public ulong CurrentId
        {
            get
            {
                var es = EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                return go != null ? UGuiSource.StableId(go) : 0;
            }
        }

        public event Action<ulong> FocusChanged;

        public void SetFocus(ulong id)
        {
            var es = EventSystem.current;
            if (es == null || id == 0) return;

            GameObject target = null;
            if (_resolve != null)
            {
                target = _resolve(id);
            }
            else
            {
                var ugui = _source as UGuiSource;
                var sel = ugui != null ? ugui.Resolve(id) : null;
                if (sel != null) target = sel.gameObject;
            }

            if (target != null && es.currentSelectedGameObject != target)
                es.SetSelectedGameObject(target);
        }

        /// <summary>No cursor of our own: the EventSystem's selection is the answer.</summary>
        public void Reconcile(IReadOnlyList<ulong> ring, int previousIndex) { }

        public int IndexOf(IReadOnlyList<ulong> ring)
        {
            if (ring == null) return -1;
            ulong id = CurrentId;
            for (int i = 0; i < ring.Count; i++)
                if (ring[i] == id) return i;
            return -1;
        }

        public void Tick(IUiSource source)
        {
            _source = source;
            ulong now = CurrentId;
            if (now == _last) return;
            _last = now;
            FocusChanged?.Invoke(now);
        }

        public void Engage() { }
        public void Release() { }
    }
}
