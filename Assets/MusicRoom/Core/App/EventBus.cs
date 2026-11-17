// Typed publish / subscribe for struct events (no boxing, no per-publish allocation). Modules talk to each other
// only through Core contracts and these events, so no module references another module's assembly.
// Each event type keeps an array of handlers, rebuilt on Subscribe / Unsubscribe (not on Publish), and every handler
// runs in its own try/catch: a throwing subscriber is logged and the remaining subscribers still get the event
// (RecorderEvent feeds the tutorial and Sunny, MenuEvent feeds Sunny and the layout: one module's bug must not look
// like another's). Publish iterates a snapshot, so handlers may subscribe or unsubscribe while it runs.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MusicRoom.Core
{
    public sealed class EventBus
    {
        abstract class Slot { public abstract int Count { get; } }

        sealed class Slot<T> : Slot
        {
            public Action<T>[] Handlers = Array.Empty<Action<T>>();
            public override int Count => Handlers.Length;
        }

        readonly Dictionary<Type, Slot> _slots = new Dictionary<Type, Slot>();

        Slot<T> GetSlot<T>(bool create)
        {
            if (_slots.TryGetValue(typeof(T), out var s)) return (Slot<T>)s;
            if (!create) return null;
            var n = new Slot<T>();
            _slots[typeof(T)] = n;
            return n;
        }

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (handler == null) return;
            var slot = GetSlot<T>(true);
            var old = slot.Handlers;
            var arr = new Action<T>[old.Length + 1];
            Array.Copy(old, arr, old.Length);
            arr[old.Length] = handler;
            slot.Handlers = arr;
        }

        /// <summary>Removes the last subscription equal to <paramref name="handler"/> (Delegate.Remove semantics).</summary>
        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var slot = GetSlot<T>(false);
            if (slot == null || handler == null) return;
            var old = slot.Handlers;
            int at = Array.LastIndexOf(old, handler);
            if (at < 0) return;
            var arr = new Action<T>[old.Length - 1];
            Array.Copy(old, 0, arr, 0, at);
            Array.Copy(old, at + 1, arr, at, old.Length - at - 1);
            slot.Handlers = arr;
        }

        public void Publish<T>(T e) where T : struct
        {
            if (!_slots.TryGetValue(typeof(T), out var s)) return;
            var handlers = ((Slot<T>)s).Handlers;
            for (int i = 0; i < handlers.Length; i++)
            {
                try { handlers[i](e); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        public int HandlerCount<T>() where T : struct =>
            _slots.TryGetValue(typeof(T), out var s) ? s.Count : 0;
    }
}
