using System;
using System.Collections.Generic;

namespace Deadfall.Rules.Common
{
    /// <summary>
    /// D-16 rejoin window. The host holds what it knew about a player who dropped (keyed by a token the player's
    /// machine keeps between sessions) for <see cref="WindowSeconds"/>. A rejoin with the same token inside the window
    /// claims it exactly once; afterwards it expires and the player counts as eliminated.
    /// </summary>
    public sealed class ReconnectLedger<T>
    {
        public const double WindowSeconds = 90.0;

        private sealed class Entry
        {
            public ulong OldClientId;
            public T Snapshot;
            public double ExpiresAt;
        }

        private readonly Dictionary<string, Entry> _held = new Dictionary<string, Entry>();
        private readonly List<string> _scratch = new List<string>();

        public int Count => _held.Count;

        public void Hold(string token, ulong oldClientId, T snapshot, double now)
        {
            if (string.IsNullOrEmpty(token)) return;
            _held[token] = new Entry { OldClientId = oldClientId, Snapshot = snapshot, ExpiresAt = now + WindowSeconds };
        }

        public bool IsHeld(string token, double now) => !string.IsNullOrEmpty(token) && _held.TryGetValue(token, out Entry e) && now < e.ExpiresAt;

        /// <summary>Seconds left for <paramref name="token"/>, or 0.</summary>
        public double TimeLeft(string token, double now) =>
            !string.IsNullOrEmpty(token) && _held.TryGetValue(token, out Entry e) ? Math.Max(0.0, e.ExpiresAt - now) : 0.0;

        /// <summary>Claims a held slot once. False when unknown, already claimed, or expired.</summary>
        public bool TryClaim(string token, double now, out ulong oldClientId, out T snapshot)
        {
            oldClientId = 0;
            snapshot = default;
            if (string.IsNullOrEmpty(token) || !_held.TryGetValue(token, out Entry e)) return false;
            _held.Remove(token);
            if (now >= e.ExpiresAt) return false;
            oldClientId = e.OldClientId;
            snapshot = e.Snapshot;
            return true;
        }

        /// <summary>Removes and returns the old client ids whose window has run out.</summary>
        public List<ulong> Expire(double now)
        {
            var gone = new List<ulong>();
            _scratch.Clear();
            foreach (KeyValuePair<string, Entry> kv in _held)
                if (now >= kv.Value.ExpiresAt) _scratch.Add(kv.Key);
            foreach (string t in _scratch)
            {
                gone.Add(_held[t].OldClientId);
                _held.Remove(t);
            }
            return gone;
        }

        public void Clear() => _held.Clear();
    }
}
