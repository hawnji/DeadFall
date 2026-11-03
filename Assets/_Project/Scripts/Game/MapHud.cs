using Deadfall.Combat;
using Deadfall.Core;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Location card (top left: map, sector, room, sectors and doors open) and a banner when a sector opens.</summary>
    public sealed class MapHud : MonoBehaviour
    {
        private float _bannerUntil;
        private string _bannerText = string.Empty;
        private MapSession _bound;

        private void Update()
        {
            MapSession s = MapSession.Current;
            if (s == _bound) return;
            if (_bound != null) _bound.SectorsChanged -= OnSectors;
            _bound = s;
            if (_bound != null) _bound.SectorsChanged += OnSectors;
        }

        private void OnDestroy()
        {
            if (_bound != null) _bound.SectorsChanged -= OnSectors;
        }

        private void OnSectors(int open, bool unlock)
        {
            if (!unlock) return;
            _bannerText = $"SECTOR {open - 1} UNLOCKED";
            _bannerUntil = Time.unscaledTime + 3.5f;
        }

        private void OnGUI()
        {
            MapSession s = MapSession.Current;
            if (s == null || !s.HasMap) return;
            HudKit.Begin();
            BuiltMap map = s.Map;
            string room = "Outside";
            int sector = -1;
            PlayerIdentity me = PlayerIdentity.Local;
            if (me != null)
            {
                int r = map.RoomAt(me.transform.position);
                if (r >= 0)
                {
                    room = map.Layout.Rooms[r].Template.DisplayName;
                    sector = map.Layout.Rooms[r].Sector;
                }
            }
            // Location card, top left.
            Color sc = sector >= 0 ? MapBuilder.SectorColor(sector) : HudKit.TextDim;
            var card = new Rect(28f, 24f, 300f, 74f);
            HudKit.AccentPanel(card, sc);
            HudKit.Label(new Rect(card.x + 16f, card.y + 6f, card.width - 32f, 20f), MapShortName() + (sector >= 0 ? $"  ·  SECTOR {sector}" : string.Empty), 12, sc, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(card.x + 16f, card.y + 24f, card.width - 32f, 26f), room, 19, Color.white, TextAnchor.MiddleLeft, head: true);
            HudKit.Label(new Rect(card.x + 16f, card.y + 50f, card.width - 32f, 18f),
                $"Sectors {s.OpenSectors}/{s.SectorCount}   ·   Doors {s.DoorsOpened}/{s.DoorCount}", 12, HudKit.TextDim, TextAnchor.MiddleLeft);

            if (Time.unscaledTime < _bannerUntil)
            {
                float a = Mathf.Clamp01((_bannerUntil - Time.unscaledTime) / 0.6f);
                float w = HudKit.W;
                float tw = HudKit.TextWidth(_bannerText, 28, head: true) + 60f;
                var b = new Rect((w - tw) * 0.5f, HudKit.H * 0.3f, tw, 48f);
                HudKit.Panel(b, a);
                HudKit.Fill(new Rect(b.x + 16f, b.yMax - 3f, b.width - 32f, 3f), new Color(HudKit.Good.r, HudKit.Good.g, HudKit.Good.b, a));
                HudKit.Label(b, _bannerText, 28, new Color(HudKit.Good.r, HudKit.Good.g, HudKit.Good.b, a), TextAnchor.MiddleCenter, head: true);
            }
            HudKit.End();
        }

        private static string MapShortName()
        {
            MapSession m = MapSession.Current;
            return m != null && m.Environment != null && m.Environment.Index == 1 ? "HARROW LINE" : "CINDER RIDGE";
        }
    }
}
