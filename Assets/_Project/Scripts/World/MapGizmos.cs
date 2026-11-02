using Deadfall.Rules.World;
using UnityEngine;

namespace Deadfall.World
{
    /// <summary>
    /// Scene-view overlay for a built map (GAME_DESIGN §10.1 debug): room outlines in sector colours, the room
    /// graph, sockets (green connected, grey capped, cyan vent), locked doors, spawn and loot points.
    /// </summary>
    public sealed class MapGizmos : MonoBehaviour
    {
        private BuiltMap _map;

        public void Bind(BuiltMap map) => _map = map;

        private void OnDrawGizmos()
        {
            if (_map == null || _map.Layout == null) return;
            Layout layout = _map.Layout;
            float cs = layout.Kit.CellSize;
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                PlacedRoom r = layout.Rooms[i];
                Bounds b = _map.RoomBounds[i];
                Gizmos.color = MapBuilder.SectorColor(r.Sector);
                Gizmos.DrawWireCube(b.center + Vector3.up * 0.05f, new Vector3(b.size.x - 0.2f, 0.1f, b.size.z - 0.2f));
                for (int s = 0; s < r.Template.Sockets.Count; s++)
                {
                    Cell c = r.SocketCell(s);
                    Dir f = r.SocketFacing(s);
                    var p = new Vector3((c.X + 0.5f + f.Dx() * 0.45f) * cs, 0.3f, (c.Z + 0.5f + f.Dz() * 0.45f) * cs);
                    bool connected = layout.IsConnected(i, s, out _);
                    Gizmos.color = !connected ? Color.grey : r.Template.Sockets[s].Type == SocketTypes.Vent ? Color.cyan : Color.green;
                    Gizmos.DrawCube(p, Vector3.one * 0.35f);
                }
            }
            foreach (Connection c in layout.Connections)
            {
                Gizmos.color = c.Locked ? Color.red : c.Secret ? Color.cyan : Color.white;
                Gizmos.DrawLine(_map.RoomCenter(c.RoomA) + Vector3.up * 0.5f, _map.RoomCenter(c.RoomB) + Vector3.up * 0.5f);
            }
            Gizmos.color = new Color(1f, 0.3f, 0.2f);
            foreach (MapSpawnPoint p in _map.EnemySpawns) Gizmos.DrawWireSphere(p.Position + Vector3.up * 0.5f, 0.4f);
            Gizmos.color = new Color(1f, 0.85f, 0.2f);
            foreach (MapLootPoint p in _map.Loot) Gizmos.DrawWireCube(p.Position + Vector3.up * 0.3f, Vector3.one * 0.6f);
            Gizmos.color = Color.green;
            foreach (Vector3 p in _map.PlayerSpawns) Gizmos.DrawWireSphere(p + Vector3.up, 0.5f);
        }
    }
}
