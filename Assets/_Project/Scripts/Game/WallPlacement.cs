using System.Collections.Generic;
using Deadfall.Core;
using Deadfall.Rules.Common;
using Deadfall.Rules.World;
using Deadfall.World;
using UnityEngine;
using UnityEngine.AI;

namespace Deadfall.Game
{
    /// <summary>
    /// Finds spots against a room's outer walls for things that stand or hang there (Gun Dispenser, ammo stations):
    /// a solid, flat stretch of wall behind the whole width, nothing in the way, a clear NavMesh standing area in front,
    /// and well away from doorways, breach closets, sector doors, switches, vents, loot and anything else listed.
    /// Uses only the map's geometry and seed, so every peer finds the same spots.
    /// </summary>
    public static class WallPlacement
    {
        /// <summary>What to fit: half width and half depth of the footprint, and its height range above the floor.</summary>
        public struct Shape
        {
            public float HalfWidth, HalfDepth, Bottom, Top;
            public Shape(float halfWidth, float halfDepth, float bottom, float top) { HalfWidth = halfWidth; HalfDepth = halfDepth; Bottom = bottom; Top = top; }
        }

        /// <summary>Gap between the wall and the object's back.</summary>
        public const float WallGap = 0.06f;
        /// <summary>Minimum distance from a doorway or breach centre (half a doorway, the object, and a walkway).</summary>
        public const float DoorClearance = 3.2f;
        /// <summary>Minimum distance from switches, vents, loot, secret panels and <c>extraAvoid</c> points.</summary>
        public const float PropClearance = 1.8f;

        private const int Mask = ~(1 << 2);
        private static readonly List<Vector3> AvoidDoors = new List<Vector3>(32);
        private static readonly List<Vector3> AvoidProps = new List<Vector3>(64);
        private static readonly List<Vector3> Spots = new List<Vector3>(32);
        private static readonly List<float> Yaws = new List<float>(32);

        /// <summary>
        /// A random spot against an outer wall of <paramref name="room"/>; <paramref name="position"/> is the floor point
        /// under the object's centre and <paramref name="yaw"/> faces into the room. False when nothing fits.
        /// </summary>
        public static bool Find(BuiltMap map, int room, Shape shape, DeterministicRandom rng, IReadOnlyList<Vector3> extraAvoid,
            out Vector3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            Physics.SyncTransforms(); // the map may have been built this frame
            Layout layout = map.Layout;
            PlacedRoom r = layout.Rooms[room];
            float cs = map.CellSize;
            float floorY = map.RoomBounds[room].min.y;

            Doorways(map, AvoidDoors);
            AvoidProps.Clear();
            foreach (DoorSwitch sw in map.Switches) if (sw != null) AvoidProps.Add(sw.transform.position);
            foreach (SecretPanel sp in map.SecretPanels) if (sp != null) AvoidProps.Add(sp.transform.position);
            foreach (CrawlVent v in map.CrawlVents) AvoidProps.Add(v.Position);
            foreach (MapLootPoint l in map.Loot) AvoidProps.Add(l.Position);
            if (extraAvoid != null) for (int i = 0; i < extraAvoid.Count; i++) AvoidProps.Add(extraAvoid[i]);

            Spots.Clear();
            Yaws.Clear();
            foreach (Cell c in r.Cells())
            {
                for (int d = 0; d < 4; d++)
                {
                    var dir = (Dir)d;
                    if (r.Contains(c.Step(dir))) continue; // not an outer wall of the room
                    var outward = new Vector3(dir.Dx(), 0f, dir.Dz());
                    // The wall is built inward from the cell edge, so its room-side face is WallThickness in from the edge line.
                    Vector3 guess = Edge(c, dir, cs) - outward * (MapBuilder.WallThickness + shape.HalfDepth + WallGap);
                    if (Near(guess, AvoidDoors, DoorClearance) || Near(guess, AvoidProps, PropClearance)) continue;
                    if (!Fits(guess, outward, floorY, shape, out Vector3 floor)) continue;
                    Spots.Add(floor);
                    Yaws.Add(Quaternion.LookRotation(-outward).eulerAngles.y);
                }
            }
            if (Spots.Count == 0) return false;
            int pick = rng.Range(0, Spots.Count);
            position = Spots[pick];
            yaw = Yaws[pick];
            return true;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the centre of every connected doorway, breach-closet opening and sector
        /// door on the map: spots that must stay clear so players and enemies can get through.
        /// </summary>
        public static void Doorways(BuiltMap map, List<Vector3> into)
        {
            into.Clear();
            Layout layout = map.Layout;
            float cs = map.CellSize;
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                PlacedRoom other = layout.Rooms[i];
                for (int s = 0; s < other.Template.Sockets.Count; s++)
                    if (layout.IsConnected(i, s, out _)) into.Add(Edge(other.SocketCell(s), other.SocketFacing(s), cs));
            }
            foreach (Breach br in layout.Breaches) into.Add(Edge(br.Inner, br.Facing, cs));
            foreach (SectorDoor d in map.Doors) if (d != null) into.Add(d.transform.position);
        }

        /// <summary>True when <paramref name="p"/> is within <paramref name="distance"/> of any of the points (flat).</summary>
        public static bool NearAny(Vector3 p, List<Vector3> points, float distance) => Near(p, points, distance);

        private static bool Fits(Vector3 at, Vector3 outward, float floorY, Shape shape, out Vector3 floor)
        {
            floor = default;
            Vector3? f = FloorPoint(at, floorY);
            if (f == null) return false;
            floor = f.Value;
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            float midY = (shape.Bottom + shape.Top) * 0.5f;
            // Flat wall right behind the back, at both sides and the middle (no doorway, corner or gap behind it).
            for (int i = -1; i <= 1; i++)
            {
                Vector3 from = floor + Vector3.up * midY + side * (i * (shape.HalfWidth - 0.1f));
                if (!Physics.Raycast(from, outward, out RaycastHit hit, shape.HalfDepth + WallGap + 0.35f, Mask, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(hit.normal, -outward) < 0.9f) return false;
            }
            Quaternion rot = Quaternion.LookRotation(-outward);
            // Footprint, shrunk a touch so the wall and the floor themselves don't count.
            float halfH = (shape.Top - shape.Bottom) * 0.5f - 0.05f;
            if (Physics.CheckBox(floor + Vector3.up * midY, new Vector3(shape.HalfWidth - 0.05f, Mathf.Max(0.05f, halfH), shape.HalfDepth - 0.05f), rot, Mask, QueryTriggerInteraction.Ignore))
                return false;
            // A clear place to stand and use it.
            Vector3 front = floor - outward * (shape.HalfDepth + 0.9f);
            if (Physics.CheckBox(front + Vector3.up * 1f, new Vector3(Mathf.Max(0.6f, shape.HalfWidth), 0.8f, 0.6f), rot, Mask, QueryTriggerInteraction.Ignore))
                return false;
            if (!NavMesh.SamplePosition(front, out NavMeshHit hitNav, 0.6f, NavMesh.AllAreas)) return false;
            return Mathf.Abs(hitNav.position.y - floor.y) <= 0.3f;
        }

        /// <summary>The floor under <paramref name="at"/> (a ray down from 1.5 m above the room's floor level), or null.</summary>
        public static Vector3? FloorPoint(Vector3 at, float floorY)
        {
            var from = new Vector3(at.x, floorY + 1.5f, at.z);
            if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, 3f, Mask, QueryTriggerInteraction.Ignore)) return null;
            if (hit.point.y > floorY + 0.5f) return null; // landed on furniture
            return hit.point;
        }

        private static Vector3 Edge(Cell c, Dir facing, float cs) =>
            new Vector3((c.X + 0.5f + facing.Dx() * 0.5f) * cs, 0f, (c.Z + 0.5f + facing.Dz() * 0.5f) * cs);

        private static bool Near(Vector3 p, List<Vector3> points, float distance)
        {
            float sq = distance * distance;
            foreach (Vector3 q in points)
            {
                float dx = p.x - q.x, dz = p.z - q.z;
                if (dx * dx + dz * dz < sq) return true;
            }
            return false;
        }

        /// <summary>Main-route rooms of a sector that can host a wall object (no side rooms, no generator rooms), airlock last.</summary>
        public static void CandidateRooms(BuiltMap map, int sector, List<int> into)
        {
            into.Clear();
            IReadOnlyList<int> taken = PowerGrid.Current != null ? PowerGrid.Current.Rooms : null;
            for (int i = 0; i < map.Layout.Rooms.Count; i++)
            {
                PlacedRoom r = map.Layout.Rooms[i];
                RoomRole role = r.Template.Role;
                if (r.Sector != sector || (role != RoomRole.Room && role != RoomRole.Objective && role != RoomRole.Spawn)) continue;
                if (map.Layout.SideDoorTo(i) >= 0) continue;
                bool gen = false;
                if (taken != null) for (int k = 0; k < taken.Count; k++) if (taken[k] == i) gen = true;
                if (!gen) into.Add(i);
            }
        }
    }
}
