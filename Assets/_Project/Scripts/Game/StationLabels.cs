using System;
using System.Collections.Generic;
using Deadfall.Core;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>
    /// Floating name tags over the interactable stations on a map (perk machines, ammo stations, syringe dispensers, the Weapon
    /// Forge, the Mod Bench, Supply Caches), in the Gun Dispenser's style: name and distance, hidden past
    /// <see cref="Range"/> metres, behind the camera, or within a few metres (the use prompt takes over there).
    /// Stations register when they're built; destroyed ones drop out on their own.
    /// </summary>
    public sealed class StationLabels : MonoBehaviour
    {
        public const float Range = 50f;
        private const float Near = 3f;

        private struct Entry
        {
            public Transform Anchor;
            public Func<string> Name;
            public Color Color;
            public float Height;
        }

        private static readonly List<Entry> Entries = new List<Entry>(32);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Entries.Clear();

        /// <summary>Tags <paramref name="anchor"/> with a label <paramref name="height"/> m above it.</summary>
        public static void Add(Transform anchor, Func<string> name, Color color, float height)
        {
            if (anchor == null || name == null) return;
            Entries.Add(new Entry { Anchor = anchor, Name = name, Color = color, Height = height });
        }

        public static void Add(Transform anchor, string name, Color color, float height) => Add(anchor, () => name, color, height);

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || UiModal.Active || Entries.Count == 0) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            bool begun = false;
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (e.Anchor == null) { Entries.RemoveAt(i); continue; }
                if (!e.Anchor.gameObject.activeInHierarchy) continue;
                Vector3 world = e.Anchor.position + Vector3.up * e.Height;
                Vector3 to = world - cam.transform.position;
                float dist = to.magnitude;
                if (dist > Range || dist < Near || Vector3.Dot(to, cam.transform.forward) <= 0f) continue;
                string name = e.Name();
                if (string.IsNullOrEmpty(name)) continue;
                if (!begun) { HudKit.Begin(); begun = true; }
                Vector2 s = HudKit.FromScreen(cam.WorldToScreenPoint(world));
                float fade = Mathf.Clamp01((Range - dist) / 10f);
                string label = $"{name.ToUpperInvariant()}  {dist:0} m";
                float w = HudKit.TextWidth(label, 12, head: true) + 20f;
                var rect = new Rect(s.x - w * 0.5f, s.y - 12f, w, 22f);
                HudKit.Panel(rect, 0.7f * fade);
                HudKit.Label(rect, label, 12, new Color(e.Color.r, e.Color.g, e.Color.b, fade), TextAnchor.MiddleCenter, head: true);
            }
            if (begun) HudKit.End();
        }
    }
}
