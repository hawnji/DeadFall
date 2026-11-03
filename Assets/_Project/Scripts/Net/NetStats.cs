using System.Collections.Generic;
using System.Text;

namespace Deadfall.Net
{
    /// <summary>
    /// Bandwidth profiling (M11): payload bytes sent and received per named message, with totals and a rate over
    /// the last second. Counts message payloads only (no transport or NGO headers), which is what our code controls.
    /// </summary>
    public sealed class NetStats
    {
        public sealed class Line
        {
            public string Name;
            public long SentBytes, ReceivedBytes;
            public int SentCount, ReceivedCount;
            public float SentPerSecond, ReceivedPerSecond;
            internal long WindowSent, WindowReceived;
        }

        private readonly Dictionary<string, Line> _lines = new Dictionary<string, Line>();
        private readonly List<Line> _sorted = new List<Line>();
        private float _windowStart;

        public long TotalSent { get; private set; }
        public long TotalReceived { get; private set; }
        public float SentPerSecond { get; private set; }
        public float ReceivedPerSecond { get; private set; }
        private long _windowSent, _windowReceived;

        public IReadOnlyCollection<Line> Lines => _lines.Values;

        public void Sent(string name, int bytes)
        {
            Line l = Get(name);
            l.SentBytes += bytes;
            l.SentCount++;
            l.WindowSent += bytes;
            TotalSent += bytes;
            _windowSent += bytes;
        }

        public void Received(string name, int bytes)
        {
            Line l = Get(name);
            l.ReceivedBytes += bytes;
            l.ReceivedCount++;
            l.WindowReceived += bytes;
            TotalReceived += bytes;
            _windowReceived += bytes;
        }

        /// <summary>Rolls the one-second rate window.</summary>
        public void Tick(float now)
        {
            float span = now - _windowStart;
            if (span < 1f) return;
            SentPerSecond = _windowSent / span;
            ReceivedPerSecond = _windowReceived / span;
            _windowSent = _windowReceived = 0;
            foreach (Line l in _lines.Values)
            {
                l.SentPerSecond = l.WindowSent / span;
                l.ReceivedPerSecond = l.WindowReceived / span;
                l.WindowSent = l.WindowReceived = 0;
            }
            _windowStart = now;
        }

        public void Reset()
        {
            _lines.Clear();
            TotalSent = TotalReceived = 0;
            SentPerSecond = ReceivedPerSecond = 0f;
            _windowSent = _windowReceived = 0;
        }

        /// <summary>"Net 3.1 KB/s up, 5.2 KB/s down" plus the busiest messages by current rate.</summary>
        public string Summary(int top = 4)
        {
            var sb = new StringBuilder(128);
            sb.Append("Net ").Append(Kb(SentPerSecond)).Append(" up, ").Append(Kb(ReceivedPerSecond)).Append(" down");
            sb.Append("  ·  total ").Append(Kb(TotalSent)).Append(" / ").Append(Kb(TotalReceived)).Append(" ");
            _sorted.Clear();
            _sorted.AddRange(_lines.Values);
            _sorted.Sort((a, b) => (b.SentPerSecond + b.ReceivedPerSecond).CompareTo(a.SentPerSecond + a.ReceivedPerSecond));
            for (int i = 0; i < _sorted.Count && i < top; i++)
            {
                Line l = _sorted[i];
                if (l.SentPerSecond + l.ReceivedPerSecond <= 0f) break;
                sb.Append(i == 0 ? "\n  " : "  ·  ").Append(l.Name).Append(' ').Append(Kb(l.SentPerSecond + l.ReceivedPerSecond));
            }
            return sb.ToString();
        }

        private static string Kb(float bytesPerSecond) => bytesPerSecond >= 1024f ? $"{bytesPerSecond / 1024f:0.0} KB/s" : $"{bytesPerSecond:0} B/s";
        private static string Kb(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576f:0.0} MB" : $"{bytes / 1024f:0.0} KB";

        private Line Get(string name)
        {
            if (!_lines.TryGetValue(name, out Line l)) _lines[name] = l = new Line { Name = name };
            return l;
        }
    }
}
