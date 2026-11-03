using System.Collections.Generic;
using Deadfall.Rules.Common;
using Deadfall.Rules.Encounters;
using Deadfall.Rules.World;
using NUnit.Framework;

namespace Deadfall.Tests.EditMode.Rules
{
    public class LayoutTests
    {
        private static readonly RoomKit Kit = CinderRidgeKit.Create();
        private static GraphTemplate Graph() => CinderRidgeKit.SurvivalGraph();

        [Test]
        public void ThousandsOfSeedsProduceValidLayouts()
        {
            GraphTemplate g = Graph();
            int fallbacks = 0;
            var hashes = new HashSet<ulong>();
            for (ulong seed = 1; seed <= 3000; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, g, seed);
                Assert.IsNotNull(l, $"seed {seed}");
                List<string> errors = LayoutValidator.Validate(l);
                Assert.IsEmpty(errors, $"seed {seed}: {string.Join("; ", errors)}");
                if (l.UsedFallback) fallbacks++;
                hashes.Add(l.Hash());
            }
            Assert.Less(fallbacks, 15, "too many seeds needed the fallback layout");
            Assert.Greater(hashes.Count, 2900, "seeds should give different layouts");
        }

        [Test]
        public void SameSeedGivesSameLayout()
        {
            for (ulong seed = 100; seed < 140; seed++)
                Assert.AreEqual(LayoutGenerator.Generate(Kit, Graph(), seed).Hash(), LayoutGenerator.Generate(Kit, Graph(), seed).Hash());
        }

        [Test]
        public void FallbackSeedIsValid()
        {
            GraphTemplate g = Graph();
            bool any = false;
            for (int a = 0; a < g.MaxAttempts && !any; a++)
            {
                Layout l = LayoutGenerator.TryBuild(Kit, g, LayoutGenerator.FallbackSeed, a);
                any = l != null && LayoutValidator.Validate(l).Count == 0;
            }
            Assert.IsTrue(any);
        }

        [Test]
        public void LayoutsHaveTheRequiredShape()
        {
            for (ulong seed = 1; seed <= 200; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, Graph(), seed);
                Assert.AreEqual(RoomRole.Spawn, l.Rooms[0].Template.Role);
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Objective));
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Boss));
                Assert.IsNotNull(l.FirstWithRole(RoomRole.Secret));
                Assert.GreaterOrEqual(l.SectorCount, 2);
                // Survival starts small: 3–5 rooms open at the start (corridors count as rooms here).
                Assert.That(l.CountInSector(0), Is.InRange(2, 8), $"seed {seed}");
                // The objective is in the last sector.
                Assert.AreEqual(l.SectorCount - 1, l.FirstWithRole(RoomRole.Objective).Sector);
            }
        }

        [Test]
        public void RotationMapsCellsAndFacings()
        {
            var t = new RoomTemplate { Id = "t", Width = 3, Depth = 2 };
            t.Sockets.Add(new SocketDef(1, 1, Dir.North));
            var r = new PlacedRoom { Template = t, Origin = new Cell(10, 20) };
            for (int rot = 0; rot < 4; rot++)
            {
                r.Rotation = rot;
                Cell c = r.SocketCell(0);
                Assert.IsTrue(r.Contains(c), $"rot {rot}");
                Assert.AreEqual((Dir)rot, r.SocketFacing(0));
                // The socket's outward neighbour is outside the room (it sits on the edge it faces).
                Assert.IsFalse(r.Contains(c.Step(r.SocketFacing(0))), $"rot {rot}");
            }
            r.Rotation = 1;
            Assert.AreEqual(2, r.Width);
            Assert.AreEqual(3, r.Depth2);
        }

        [Test]
        public void EveryKitSocketSitsOnTheEdgeItFaces()
        {
            foreach (RoomTemplate t in Kit.Templates)
                foreach (SocketDef s in t.Sockets)
                {
                    // The socket cell is part of the room and the cell it faces is not (works for shaped rooms too).
                    bool onEdge = t.HasCell(s.X, s.Z) && !t.HasCell(s.X + s.Facing.Dx(), s.Z + s.Facing.Dz());
                    Assert.IsTrue(onEdge, $"{t.Id} socket ({s.X},{s.Z}) {s.Facing}");
                }
        }

        [Test]
        public void ValidatorCatchesOverlapAndBrokenConnections()
        {
            Layout l = LayoutGenerator.Generate(Kit, Graph(), 42);
            Assert.IsEmpty(LayoutValidator.Validate(l));
            Cell saved = l.Rooms[1].Origin;
            l.Rooms[1].Origin = l.Rooms[0].Origin;
            List<string> errors = LayoutValidator.Validate(l);
            Assert.IsTrue(errors.Exists(e => e.Contains("overlap")));
            Assert.IsTrue(errors.Exists(e => e.Contains("adjacent") || e.Contains("face")));
            l.Rooms[1].Origin = saved;
            l.Connections.RemoveAt(0);
            Assert.IsTrue(LayoutValidator.Validate(l).Exists(e => e.Contains("unreachable")));
        }

        [Test]
        public void ValidatorCatchesSectorsOpeningOutOfOrder()
        {
            Layout l = LayoutGenerator.Generate(Kit, Graph(), 7);
            for (int i = 0; i < l.Connections.Count; i++)
            {
                if (!l.Connections[i].Locked) continue;
                Connection c = l.Connections[i];
                c.UnlockSector = 0;
                l.Connections[i] = c;
                break;
            }
            Assert.IsNotEmpty(LayoutValidator.Validate(l));
        }

        [Test]
        public void SpawnSectorAlwaysHasASupplyCache()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, Graph(), seed);
                Assert.IsTrue(l.Loot.Exists(x => x.Kind == LootKind.SupplyCache && l.Rooms[x.Room].Sector == 0), $"seed {seed}");
            }
        }

        [Test]
        public void SectorScheduleOpensInOrder()
        {
            Assert.AreEqual(1, SectorSchedule.OpenSectors(0, 4));
            Assert.AreEqual(1, SectorSchedule.OpenSectors(2, 4));
            Assert.AreEqual(2, SectorSchedule.OpenSectors(3, 4));
            Assert.AreEqual(3, SectorSchedule.OpenSectors(7, 4));
            Assert.AreEqual(4, SectorSchedule.OpenSectors(12, 4));
            Assert.AreEqual(4, SectorSchedule.OpenSectors(99, 4));
            Assert.AreEqual(2, SectorSchedule.OpenSectors(99, 2));
        }

        [Test]
        public void MapsUseShapedRoomsAndMaskedCells()
        {
            int shaped = 0;
            for (ulong seed = 1; seed <= 100; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, Graph(), seed);
                foreach (PlacedRoom r in l.Rooms)
                {
                    if (r.Template.IsShaped) shaped++;
                    int n = 0;
                    foreach (Cell c in r.Cells())
                    {
                        n++;
                        r.ToLocal(c, out int lx, out int lz);
                        Assert.IsTrue(r.Template.HasCell(lx, lz));
                        Assert.AreEqual(c, r.ToWorld(lx, lz), "ToLocal must invert ToWorld");
                        Assert.IsTrue(r.Contains(c));
                    }
                    Assert.AreEqual(r.Template.CellCount, n);
                    Assert.IsTrue(r.Contains(r.CenterCell));
                }
            }
            Assert.Greater(shaped, 100, "most maps should contain L, T, Z or plus rooms");
        }

        [Test]
        public void BreachClosetsSitOutsideRoomsAndAreSpread()
        {
            for (ulong seed = 1; seed <= 300; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, Graph(), seed);
                Assert.Greater(l.Breaches.Count, l.SectorCount, $"seed {seed}");
                var closets = new HashSet<Cell>();
                foreach (Breach b in l.Breaches)
                {
                    Assert.AreEqual(b.Room, l.RoomAt(b.Inner), $"seed {seed}: breach inner cell is not in its room");
                    Assert.AreEqual(-1, l.RoomAt(b.Closet), $"seed {seed}: closet overlaps a room");
                    Assert.IsTrue(closets.Add(b.Closet), $"seed {seed}: two breaches share a closet");
                }
            }
        }

        [Test]
        public void DoorsAreLockedByCreditsSwitchesOrPower()
        {
            int side = 0, special = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                Layout l = LayoutGenerator.Generate(Kit, Graph(), seed);
                foreach (Connection c in l.Connections)
                {
                    if (c.Locked) Assert.IsTrue(c.IsDoor, $"seed {seed}: sector door without a lock");
                    if (c.Lock == DoorLock.Credits) Assert.Greater(c.Cost, 0);
                    if (c.Lock == DoorLock.Switch || c.Lock == DoorLock.Power)
                    {
                        special++;
                        Assert.IsTrue(c.Locked, "only sector doors use switches");
                        PlacedRoom sw = l.Rooms[c.SwitchRoom];
                        Assert.IsTrue(sw.OnCriticalPath);
                        Assert.Less(sw.Sector, c.UnlockSector, "the switch must be reachable before the door opens");
                    }
                    if (c.IsDoor && !c.Locked && !c.Secret) side++;
                }
            }
            Assert.Greater(side, 100, "branch rooms should often sit behind paid side doors");
            Assert.Greater(special, 50, "some sector doors should need a switch or power relay");
        }

        [Test]
        public void BreachSpawnsMixNearestAndNearbyBreaches()
        {
            // 12 breaches 5 m apart; 2 unusable (sector closed).
            var d = new List<float>();
            for (int i = 0; i < 12; i++) d.Add(i * 5f);
            d[1] = -1f; d[11] = -1f;
            var rng = new DeterministicRandom(9);
            var counts = new int[12];
            for (int i = 0; i < 4000; i++) counts[BreachSpawns.Pick(d, rng)]++;
            Assert.AreEqual(0, counts[1]);
            Assert.AreEqual(0, counts[11]);
            // Nearest three (0, 2, 3) get the close share on top of the spread share.
            Assert.Greater(counts[0], counts[8]);
            Assert.Greater(counts[3], counts[8]);
            // Everything within 40 m is used; 50 m (index 10) is not, since 8 breaches are within range.
            for (int i = 2; i <= 8; i++) Assert.Greater(counts[i], 0, $"breach {i}");
            Assert.AreEqual(0, counts[10]);
            Assert.AreEqual(-1, BreachSpawns.Pick(new List<float> { -1f, -1f }, rng));
        }

        [Test]
        public void BreachSpawnsFallBackToNearestWhenAllAreFar()
        {
            var d = new List<float>();
            for (int i = 0; i < 10; i++) d.Add(100f + i);
            var rng = new DeterministicRandom(3);
            for (int i = 0; i < 2000; i++) Assert.Less(BreachSpawns.Pick(d, rng), BreachSpawns.MinSpread);
        }
    }
}
