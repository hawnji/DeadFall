using System;
using System.Collections.Generic;
using UnityEngine;

namespace Deadfall.Core
{
    /// <summary>
    /// Allocation-free publish/subscribe for struct events. Systems use it for upward or sideways
    /// notifications (enemy killed, wave started, hit confirmed) so they never reference each other.
    /// <code>EventBus&lt;HitConfirmed&gt;.Subscribe(OnHit); EventBus&lt;HitConfirmed&gt;.Publish(evt);</code>
    /// </summary>
    public static class EventBus<T> where T : struct
    {
        private static readonly List<Action<T>> Handlers = new List<Action<T>>(8);
        private static bool _registered;

        public static void Subscribe(Action<T> handler)
        {
            if (handler == null) return;
            if (!_registered)
            {
                EventBusRegistry.RegisterClear(Clear);
                _registered = true;
            }
            if (!Handlers.Contains(handler)) Handlers.Add(handler);
        }

        public static void Unsubscribe(Action<T> handler) => Handlers.Remove(handler);

        public static void Publish(T evt)
        {
            // Iterate backwards so handlers may unsubscribe during dispatch.
            for (int i = Handlers.Count - 1; i >= 0; i--)
            {
                if (i >= Handlers.Count) continue;
                try
                {
                    Handlers[i](evt);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public static int HandlerCount => Handlers.Count;

        private static void Clear()
        {
            Handlers.Clear();
            _registered = false;
        }
    }

    /// <summary>Clears every EventBus channel when play mode starts (safe with domain reload disabled).</summary>
    public static class EventBusRegistry
    {
        private static readonly List<Action> ClearActions = new List<Action>();

        internal static void RegisterClear(Action clear) => ClearActions.Add(clear);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ClearAll()
        {
            for (int i = 0; i < ClearActions.Count; i++) ClearActions[i]();
            ClearActions.Clear();
        }
    }
}
