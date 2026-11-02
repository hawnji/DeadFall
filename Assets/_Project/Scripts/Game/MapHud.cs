using Deadfall.Combat;
using Deadfall.World;
using UnityEngine;

namespace Deadfall.Game
{
    /// <summary>Map line under the wave HUD (room name, sectors, next unlock) and a banner when a sector opens.</summary>
    public sealed class MapHud : MonoBehaviour
    {
        private GUIStyle _line, _banner;
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
            if (_line == null)
            {
                _line = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperLeft };
                _banner = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }
            BuiltMap map = s.Map;
            string room = string.Empty;
            PlayerIdentity me = PlayerIdentity.Local;
            if (me != null)
            {
                int r = map.RoomAt(me.transform.position);
                if (r >= 0) room = map.Layout.Rooms[r].Template.DisplayName + "  ·  ";
            }
            string next = s.NextUnlockSurge > 0 ? $"  ·  next sector after surge {s.NextUnlockSurge}" : "  ·  all sectors open";
            GUI.Label(new Rect(24f, Screen.height - 104f, 700f, 22f), $"Cinder Ridge  ·  {room}sectors {s.OpenSectors}/{s.SectorCount}{next}", _line);

            if (Time.unscaledTime < _bannerUntil)
            {
                float a = Mathf.Clamp01((_bannerUntil - Time.unscaledTime) / 0.6f);
                GUI.color = new Color(0.55f, 0.95f, 0.6f, a);
                GUI.Label(new Rect(0f, Screen.height * 0.22f, Screen.width, 40f), _bannerText, _banner);
                GUI.color = Color.white;
            }
        }
    }
}
