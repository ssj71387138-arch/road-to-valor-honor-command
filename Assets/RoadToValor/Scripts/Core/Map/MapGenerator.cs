using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>
    /// Builds the 1200m x 1200m battlefield from MapLayout + GameConfig, deterministically.
    /// Output: heightfield (snow mountains, muddy centre, rivers, swamp, coast), nav grid,
    /// props (ruins, trenches, wrecks, forests, factories, rail yard...) and resampled lane paths.
    /// </summary>
    public static class MapGenerator
    {
        public static MapData Generate(MapLayout layout, GameConfig cfg)
        {
            var map = new MapData
            {
                Config = cfg,
                Layout = layout,
                Size = cfg.MapSize,
                Half = cfg.MapSize * 0.5f,
                HeightRes = cfg.HeightResolution,
            };
            map.HeightCell = map.Size / (map.HeightRes - 1);
            int seed = cfg.MapSeed;
            var rng = new Rng(seed);

            // 1) Macro heights (used for navigation steepness)
            int n = map.HeightRes;
            var macro = new float[n * n];
            for (int zi = 0; zi < n; zi++)
            {
                float z = -map.Half + zi * map.HeightCell;
                for (int xi = 0; xi < n; xi++)
                {
                    float x = -map.Half + xi * map.HeightCell;
                    macro[zi * n + xi] = MacroHeight(layout, cfg, x, z, seed);
                }
            }

            // 2) Micro detail (trenches, craters) on top of macro heights.
            map.Heights = (float[])macro.Clone();
            PlanTrenches(map, rng);
            PlanCraters(map, rng);
            foreach (var line in map.TrenchLines) CarvePolyline(map, line, 1.25f, 1.7f);
            foreach (var c in map.Craters) CarveCrater(map, c, 2.5f + (Math.Abs(c.X * 13.1f + c.Z * 7.3f) % 4f));

            // 3) Road / trail distance fields at nav resolution.
            int navW = (int)MathF.Round(map.Size / cfg.NavCellSize);
            map.Nav = new NavGrid(navW, navW, cfg.NavCellSize, new Vec2(-map.Half, -map.Half));
            map.RoadDist = new float[navW * navW];
            map.TrailDist = new float[navW * navW];
            for (int i = 0; i < map.RoadDist.Length; i++)
            {
                Vec2 p = map.Nav.CellCenter(i);
                float rd = 999f, td = 999f;
                foreach (var lane in layout.Lanes) rd = Math.Min(rd, MathUtil.DistanceToPolyline(p, lane.Points));
                foreach (var t in layout.Trails) td = Math.Min(td, MathUtil.DistanceToPolyline(p, t));
                map.RoadDist[i] = rd;
                map.TrailDist[i] = td;
            }

            // 4) Nav grid from macro heights.
            BuildNav(map, macro);

            // 5) Props (some of them block navigation).
            PlaceProps(map, rng);
            foreach (var prop in map.Props)
                if (prop.BlockRadius > 0f) StampObstacle(map.Nav, prop.Pos, prop.BlockRadius);
            foreach (var hq in layout.Hqs) StampObstacle(map.Nav, hq.Position, 14f);
            foreach (var zone in layout.Zones)
                foreach (var pad in zone.PadPositions) StampObstacle(map.Nav, pad, 4.5f);

            // 6) Lane paths on the final grid.
            BuildLanes(map);
            return map;
        }

        // ------------------------------------------------------------------ heights

        private static float BoundaryMountain(MapLayout l, float x, float z)
        {
            float side = Math.Abs(x) - l.PlayableHalfWidth;
            float sideMountain = MathUtil.SmoothStep(l.MountainSouthLimit - 70f, l.MountainSouthLimit + 70f, z);
            float north = z - l.NorthEdge;
            float corner = Math.Min(Math.Abs(x) - 330f, z - 492f);
            float m = Math.Max(Math.Max(north, side * sideMountain), corner);
            return MathUtil.Clamp01(m / 45f);
        }

        private static float BoundarySea(MapLayout l, float x, float z)
        {
            float side = Math.Abs(x) - l.PlayableHalfWidth;
            float sideSea = 1f - MathUtil.SmoothStep(l.MountainSouthLimit - 70f, l.MountainSouthLimit + 70f, z);
            float south = l.SouthEdge - z;
            float corner = Math.Min(Math.Abs(x) - 340f, -478f - z);
            float s = Math.Max(Math.Max(south, side * sideSea), corner);
            return MathUtil.Clamp01(s / 28f);
        }

        public static float MacroHeight(MapLayout l, GameConfig cfg, float x, float z, int seed)
        {
            float b = cfg.BaseHeight;
            float h = b + (Noise.Fbm(x / 170f, z / 170f, seed, 4) - 0.5f) * 7f + (Noise.Fbm(x / 42f, z / 42f, seed + 7, 3) - 0.5f) * 2.2f;
            h += MathUtil.SmoothStep(150f, 480f, z) * 2.5f; // snowy northern plateau

            var p = new Vec2(x, z);

            foreach (var hill in l.Hills)
            {
                float d = Vec2.Distance(p, hill.Center) / hill.Radius;
                if (d > 2.2f) continue;
                float bump = MathF.Exp(-d * d * 2.2f);
                h += hill.Height * bump * (0.8f + 0.4f * Noise.Fbm(x / 25f, z / 25f, seed + 3, 2));
            }

            foreach (var zone in l.Zones)
            {
                float d = Vec2.Distance(p, zone.Center);
                if (d > zone.Radius * 1.1f) continue;
                float target = b + BiomeOffset(zone.Biome) + (Noise.Fbm(x / 30f, z / 30f, seed + 5, 2) - 0.5f) * 1.2f;
                float wgt = 1f - MathUtil.SmoothStep(zone.Radius * 0.55f, zone.Radius * 1.08f, d);
                h = MathUtil.Lerp(h, target, wgt);
            }

            foreach (var hq in l.Hqs)
            {
                float d = Vec2.Distance(p, hq.Position);
                if (d > hq.Radius * 1.45f) continue;
                float wgt = 1f - MathUtil.SmoothStep(hq.Radius * 0.9f, hq.Radius * 1.45f, d);
                h = MathUtil.Lerp(h, b + 2.5f, wgt);
            }

            // Mild flattening along lanes so roads read clearly.
            float laneD = 999f;
            foreach (var lane in l.Lanes) laneD = Math.Min(laneD, MathUtil.DistanceToPolyline(p, lane.Points));
            if (laneD < 14f)
            {
                float wgt = (1f - MathUtil.SmoothStep(6f, 14f, laneD)) * 0.6f;
                h = MathUtil.Lerp(h, Math.Max(b, Math.Min(h, b + 3f)), wgt);
            }

            // Map boundary: snow mountains in the north and on the northern flanks, sea in the south.
            float mount = BoundaryMountain(l, x, z);
            if (mount > 0f)
            {
                float ridge = 1f - Math.Abs(Noise.Fbm(x / 70f, z / 70f, seed + 11, 4) * 2f - 1f);
                h += MathUtil.SmoothStep(0f, 1f, mount) * (40f + ridge * 75f);
            }
            float sea = BoundarySea(l, x, z);
            if (sea > 0f) h = MathUtil.Lerp(h, cfg.WaterLevel - 6f, MathUtil.SmoothStep(0f, 1f, sea));

            // Swamp pools.
            foreach (var zone in l.Zones)
            {
                if (zone.Biome != "swamp") continue;
                float d = Vec2.Distance(p, zone.Center);
                if (d > zone.Radius * 1.25f) continue;
                float wgt = 1f - MathUtil.SmoothStep(zone.Radius * 0.7f, zone.Radius * 1.25f, d);
                float pool = Noise.Fbm(x / 14f, z / 14f, seed + 21, 3);
                float swampH = cfg.WaterLevel + 0.35f - MathUtil.SmoothStep(0.58f, 0.72f, pool) * 1.6f;
                // keep the capture centre and the pads dry
                if (d < zone.Radius * 0.35f) swampH = cfg.WaterLevel + 0.6f;
                foreach (var pad in zone.PadPositions)
                    if (Vec2.Distance(p, pad) < 12f) swampH = cfg.WaterLevel + 0.7f;
                h = MathUtil.Lerp(h, swampH, wgt);
            }

            // Rivers.
            foreach (var r in l.Rivers)
            {
                float d = MathUtil.DistanceToPolyline(p, r.Points);
                float halfW = r.Width * 0.5f;
                const float bank = 7f;
                if (d > halfW + bank) continue;
                float bed = cfg.WaterLevel - 2.6f;
                float profile;
                if (d < halfW) profile = bed + 1.6f * (d / halfW) * (d / halfW);
                else profile = MathUtil.Lerp(cfg.WaterLevel - 1.0f, h, MathUtil.SmoothStep(0f, 1f, (d - halfW) / bank));
                h = Math.Min(h, profile);
            }

            // Fords: shallow crossings.
            foreach (var f in l.Fords)
            {
                float d = Vec2.Distance(p, f.Center);
                if (d > f.Radius) continue;
                float wgt = 1f - MathUtil.SmoothStep(f.Radius * 0.6f, f.Radius, d);
                h = Math.Max(h, MathUtil.Lerp(h, cfg.WaterLevel - 0.4f, wgt));
            }
            return h;
        }

        private static float BiomeOffset(string biome)
        {
            switch (biome)
            {
                case "highland": return 6f;
                case "snow_mountain": return 5f;
                case "snow_fort": return 2f;
                case "strategic": return 1.2f;
                case "swamp": return -1.8f;
                case "coast": return -0.8f;
                default: return 0f;
            }
        }

        private static void PlanTrenches(MapData map, Rng rng)
        {
            foreach (var zone in map.Layout.Zones)
            {
                int lines;
                switch (zone.Biome)
                {
                    case "trench": lines = 3; break;
                    case "strategic": lines = 2; break;
                    case "fort": case "wall": case "highland": case "snow_fort": lines = 1; break;
                    default: lines = 0; break;
                }
                for (int k = 0; k < lines; k++)
                {
                    // Trenches run east-west, zig-zagging, facing the enemy side.
                    float offZ = (k - (lines - 1) * 0.5f) * 18f + rng.Range(-3f, 3f);
                    if (zone.Biome != "trench" && zone.Biome != "strategic") offZ = zone.Center.Z >= 0 ? -zone.Radius * 0.55f : zone.Radius * 0.55f;
                    float halfLen = zone.Radius * 0.8f;
                    var line = new List<Vec2>();
                    int segs = 8;
                    for (int s = 0; s <= segs; s++)
                    {
                        float t = s / (float)segs;
                        float x = zone.Center.X - halfLen + t * halfLen * 2f;
                        float zig = (s % 2 == 0 ? 1f : -1f) * 3f;
                        line.Add(new Vec2(x, zone.Center.Z + offZ + zig));
                    }
                    map.TrenchLines.Add(line);
                }
            }
        }

        private static void PlanCraters(MapData map, Rng rng)
        {
            int placed = 0, guard = 0;
            while (placed < 260 && guard++ < 4000)
            {
                var p = new Vec2(rng.Range(-520f, 520f), rng.Range(-430f, 430f));
                float bias = 1f - Math.Abs(p.Z) / 520f; // more craters near the front line
                if (!rng.Chance(0.35f + bias * 0.6f)) continue;
                bool bad = false;
                foreach (var r in map.Layout.Rivers) if (MathUtil.DistanceToPolyline(p, r.Points) < 16f) { bad = true; break; }
                foreach (var b in map.Layout.Bridges) if (b.Contains(p, 8f)) { bad = true; break; }
                foreach (var z in map.Layout.Zones)
                    foreach (var pad in z.PadPositions) if (Vec2.Distance(pad, p) < 12f) bad = true;
                foreach (var hq in map.Layout.Hqs) if (Vec2.Distance(hq.Position, p) < hq.Radius + 10f) bad = true;
                if (bad) continue;
                map.Craters.Add(p);
                placed++;
            }
        }

        private static void CarvePolyline(MapData map, List<Vec2> line, float halfWidth, float depth)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var p in line) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z); }
            ForHeightSamples(map, minX - 4, maxX + 4, minZ - 4, maxZ + 4, (i, p) =>
            {
                float d = MathUtil.DistanceToPolyline(p, line);
                if (d < halfWidth + 1.5f)
                {
                    float k = 1f - MathUtil.SmoothStep(halfWidth * 0.6f, halfWidth + 1.5f, d);
                    map.Heights[i] -= depth * k;
                }
            });
        }

        private static void CarveCrater(MapData map, Vec2 c, float radius)
        {
            ForHeightSamples(map, c.X - radius * 1.6f, c.X + radius * 1.6f, c.Z - radius * 1.6f, c.Z + radius * 1.6f, (i, p) =>
            {
                float d = Vec2.Distance(p, c) / radius;
                if (d < 1f) map.Heights[i] -= (1f - d * d) * radius * 0.35f;
                else if (d < 1.5f) map.Heights[i] += (1f - Math.Abs(d - 1.2f) / 0.3f) * 0.35f * Math.Max(0f, 1f);
            });
        }

        private static void ForHeightSamples(MapData map, float minX, float maxX, float minZ, float maxZ, Action<int, Vec2> f)
        {
            int n = map.HeightRes;
            int x0 = MathUtil.Clamp((int)MathF.Floor((minX + map.Half) / map.HeightCell), 0, n - 1);
            int x1 = MathUtil.Clamp((int)MathF.Ceiling((maxX + map.Half) / map.HeightCell), 0, n - 1);
            int z0 = MathUtil.Clamp((int)MathF.Floor((minZ + map.Half) / map.HeightCell), 0, n - 1);
            int z1 = MathUtil.Clamp((int)MathF.Ceiling((maxZ + map.Half) / map.HeightCell), 0, n - 1);
            for (int zi = z0; zi <= z1; zi++)
                for (int xi = x0; xi <= x1; xi++)
                    f(zi * n + xi, new Vec2(-map.Half + xi * map.HeightCell, -map.Half + zi * map.HeightCell));
        }

        // ------------------------------------------------------------------ navigation

        private static float SampleMacro(MapData map, float[] macro, float x, float z)
        {
            float fx = (x + map.Half) / map.HeightCell, fz = (z + map.Half) / map.HeightCell;
            int n = map.HeightRes;
            int x0 = MathUtil.Clamp((int)MathF.Floor(fx), 0, n - 2), z0 = MathUtil.Clamp((int)MathF.Floor(fz), 0, n - 2);
            float tx = MathUtil.Clamp01(fx - x0), tz = MathUtil.Clamp01(fz - z0);
            int i = z0 * n + x0;
            float ab = macro[i] + (macro[i + 1] - macro[i]) * tx;
            float cd = macro[i + n] + (macro[i + n + 1] - macro[i + n]) * tx;
            return ab + (cd - ab) * tz;
        }

        private static void BuildNav(MapData map, float[] macro)
        {
            var nav = map.Nav;
            var cfg = map.Config;
            var l = map.Layout;
            for (int i = 0; i < nav.Flags.Length; i++)
            {
                Vec2 p = nav.CellCenter(i);
                float h = SampleMacro(map, macro, p.X, p.Z);
                float hx = SampleMacro(map, macro, p.X + 2f, p.Z) - SampleMacro(map, macro, p.X - 2f, p.Z);
                float hz = SampleMacro(map, macro, p.X, p.Z + 2f) - SampleMacro(map, macro, p.X, p.Z - 2f);
                float slope = MathF.Sqrt(hx * hx + hz * hz) / 4f;
                NavFlags f = NavFlags.None;

                if (Math.Abs(p.X) > l.PlayableHalfWidth + 4f || p.Z > l.NorthEdge + 4f || p.Z < l.SouthEdge - 2f ||
                    BoundaryMountain(l, p.X, p.Z) > 0.12f || BoundarySea(l, p.X, p.Z) > 0.25f)
                    f |= NavFlags.OutOfBounds;

                if (h < cfg.WaterLevel - 0.25f) f |= NavFlags.Water;
                foreach (var ford in l.Fords) if (Vec2.Distance(p, ford.Center) <= ford.Radius) f |= NavFlags.Ford;
                foreach (var b in l.Bridges) if (b.Contains(p, 0.5f)) f |= NavFlags.Bridge;
                if (slope > 0.78f || h > cfg.BaseHeight + 42f) f |= NavFlags.Steep;
                if (map.RoadDist[i] < 7f) f |= NavFlags.Road;
                f |= NavFlags.Walkable;
                nav.Flags[i] = f;

                // Terrain movement cost.
                float cost = 1.15f;
                if ((f & NavFlags.Road) != 0) cost = 1f;
                else
                {
                    if (Math.Abs(p.Z) < 170f) cost = 1.25f;         // churned mud
                    if (p.Z > 300f || h > cfg.BaseHeight + 12f) cost = 1.3f; // deep snow
                    var zone = map.ZoneAt(p);
                    if (zone != null && zone.Biome == "swamp") cost = 1.6f;
                }
                if ((f & NavFlags.Ford) != 0) cost = 1.8f;
                if ((f & NavFlags.Bridge) != 0) cost = 1f;
                nav.Cost[i] = cost;
            }
        }

        private static void StampObstacle(NavGrid nav, Vec2 c, float radius)
        {
            nav.WorldToCell(c - new Vec2(radius, radius), out int x0, out int z0);
            nav.WorldToCell(c + new Vec2(radius, radius), out int x1, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!nav.InBounds(x, z)) continue;
                    if (Vec2.Distance(nav.CellCenter(x, z), c) <= radius + nav.CellSize * 0.35f)
                        nav.Flags[nav.Index(x, z)] |= NavFlags.Obstacle;
                }
        }

        // ------------------------------------------------------------------ props

        private sealed class PropContext
        {
            public MapData Map;
            public Rng Rng;
        }

        private static bool IsClear(MapData map, Vec2 p, float clearance, bool blocking)
        {
            var l = map.Layout;
            if (Math.Abs(p.X) > map.Half - 4f || Math.Abs(p.Z) > map.Half - 4f) return false;
            float h = map.SampleHeight(p);
            if (h < map.WaterLevel + 0.2f) return false;
            foreach (var r in l.Rivers) if (MathUtil.DistanceToPolyline(p, r.Points) < r.Width * 0.5f + 4f + clearance) return false;
            foreach (var b in l.Bridges) if (b.Contains(p, 6f + clearance)) return false;
            foreach (var hq in l.Hqs) if (Vec2.Distance(p, hq.Position) < hq.Radius * 1.1f + clearance) return false;
            foreach (var z in l.Zones)
                foreach (var pad in z.PadPositions) if (Vec2.Distance(pad, p) < 11f + clearance) return false;
            if (blocking)
            {
                int ni = map.Nav.CellIndexAt(p);
                if (ni >= 0 && map.RoadDist[ni] < 11f + clearance) return false;
                if (ni >= 0 && map.TrailDist[ni] < 5f + clearance) return false;
                foreach (var z in l.Zones) if (Vec2.Distance(p, z.Center) < z.Radius * 0.3f + clearance) return false;
            }
            else
            {
                int ni = map.Nav.CellIndexAt(p);
                if (ni >= 0 && map.RoadDist[ni] < 6.5f) return false;
            }
            return true;
        }

        private static bool Add(PropContext c, PropKind kind, Vec2 p, float yaw, float scale, float block, int variant = 0, float tilt = 0f)
        {
            if (!IsClear(c.Map, p, block, block > 0f)) return false;
            bool snowy = p.Z > 120f || c.Map.SampleHeight(p) > c.Map.Config.BaseHeight + 14f;
            c.Map.Props.Add(new PropInstance { Kind = kind, Pos = p, Yaw = yaw, Scale = scale, BlockRadius = block, Variant = variant, Tilt = tilt, Snowy = snowy });
            return true;
        }

        private static void AddUnchecked(PropContext c, PropKind kind, Vec2 p, float yaw, float scale, float block, int variant = 0, float tilt = 0f)
        {
            bool snowy = p.Z > 120f || c.Map.SampleHeight(p) > c.Map.Config.BaseHeight + 14f;
            c.Map.Props.Add(new PropInstance { Kind = kind, Pos = p, Yaw = yaw, Scale = scale, BlockRadius = block, Variant = variant, Tilt = tilt, Snowy = snowy });
        }

        private static Vec2 InZone(PropContext c, ZoneDef z, float minFrac, float maxFrac)
        {
            float a = c.Rng.Range(0f, MathUtil.TwoPi);
            float r = c.Rng.Range(minFrac, maxFrac) * z.Radius;
            return z.Center + new Vec2(MathF.Sin(a) * r, MathF.Cos(a) * r);
        }

        private static void Scatter(PropContext c, ZoneDef z, PropKind kind, int count, float minFrac, float maxFrac, float scaleMin, float scaleMax, float block, int attempts = 6)
        {
            for (int i = 0; i < count; i++)
            {
                for (int t = 0; t < attempts; t++)
                {
                    var p = InZone(c, z, minFrac, maxFrac);
                    float s = c.Rng.Range(scaleMin, scaleMax);
                    if (Add(c, kind, p, c.Rng.Range(0f, MathUtil.TwoPi), s, block * s, c.Rng.Range(0, 4), 0f)) break;
                }
            }
        }

        private static void PlaceProps(MapData map, Rng rng)
        {
            var c = new PropContext { Map = map, Rng = rng };
            var l = map.Layout;

            foreach (var z in l.Zones) PlaceBiome(c, z);

            // HQ walls with gates toward each lane.
            foreach (var hq in l.Hqs)
            {
                var gates = new List<float>();
                foreach (var lane in l.Lanes)
                {
                    // first lane point further than 70m from the HQ
                    var pts = lane.Points;
                    Vec2 target = pts[pts.Count / 2];
                    if (hq.Team == Team.Player) { foreach (var p in pts) if (Vec2.Distance(p, hq.Position) > 70f) { target = p; break; } }
                    else { for (int i = pts.Count - 1; i >= 0; i--) if (Vec2.Distance(pts[i], hq.Position) > 70f) { target = pts[i]; break; } }
                    gates.Add((target - hq.Position).ToYaw());
                }
                map.HqGateYaws[(int)hq.Team] = gates.ToArray();
                float wallR = hq.Radius * 0.78f;
                int segs = 40;
                for (int s = 0; s < segs; s++)
                {
                    float yaw = s / (float)segs * MathUtil.TwoPi;
                    bool gate = false;
                    foreach (float g in gates) if (Math.Abs(MathUtil.DeltaAngle(yaw, g)) < 0.24f) gate = true;
                    if (gate) continue;
                    Vec2 p = hq.Position + Vec2.FromYaw(yaw) * wallR;
                    bool tower = s % 5 == 0;
                    AddUnchecked(c, tower ? PropKind.HqTower : PropKind.HqWall, p, yaw + MathUtil.Pi * 0.5f, 1f, tower ? 3.2f : 2.6f);
                }
            }

            // Forward supply depots (전방 보급 기지).
            foreach (var d in l.SupplyDepots)
            {
                var fake = new ZoneDef { Center = d.Center, Radius = 26f, Biome = "supply" };
                Scatter(c, fake, PropKind.Tent, 3, 0.2f, 0.9f, 0.9f, 1.1f, 3.2f);
                Scatter(c, fake, PropKind.CrateStack, 6, 0.1f, 1f, 0.8f, 1.2f, 1.6f);
                Scatter(c, fake, PropKind.FuelDrums, 4, 0.1f, 1f, 0.9f, 1.1f, 0f);
                Scatter(c, fake, PropKind.ParkedTruck, 2, 0.3f, 1f, 1f, 1f, 2.6f);
                Scatter(c, fake, PropKind.Sandbags, 4, 0.8f, 1.1f, 1f, 1.3f, 0f);
            }

            // Telegraph poles along lanes.
            foreach (var lane in l.Lanes)
            {
                float acc = 0f;
                for (int i = 0; i < lane.Points.Count - 1; i++)
                {
                    Vec2 a = lane.Points[i], b = lane.Points[i + 1];
                    float len = Vec2.Distance(a, b);
                    Vec2 dir = (b - a).Normalized;
                    for (float t = 0; t < len; t += 6f)
                    {
                        acc += 6f;
                        if (acc < 42f) continue;
                        acc = 0f;
                        Vec2 p = a + dir * t + dir.PerpRight * 9.5f;
                        if (rng.Chance(0.75f)) AddUncheckedIfDry(c, PropKind.TelegraphPole, p, dir.ToYaw(), 1f, rng.Chance(0.2f) ? 0.25f : 0f);
                    }
                }
            }

            // Wrecks along the lanes.
            foreach (var lane in l.Lanes)
            {
                for (int i = 0; i < 12; i++)
                {
                    int seg = rng.Range(1, lane.Points.Count - 2);
                    Vec2 a = lane.Points[seg], b = lane.Points[seg + 1];
                    Vec2 p = Vec2.Lerp(a, b, rng.NextFloat());
                    Vec2 dir = (b - a).Normalized;
                    p = p + dir.PerpRight * (rng.Chance(0.5f) ? 1f : -1f) * rng.Range(14f, 26f);
                    PropKind k = rng.Chance(0.5f) ? PropKind.WreckedTank : (rng.Chance(0.5f) ? PropKind.WreckedTruck : PropKind.WreckedHalftrack);
                    Add(c, k, p, rng.Range(0f, MathUtil.TwoPi), 1f, 3f, rng.Range(0, 3), rng.Range(-0.15f, 0.15f));
                }
            }

            // Jungle forests between the lanes + mountain flank forests.
            int clusters = 0, guard = 0;
            while (clusters < 95 && guard++ < 3000)
            {
                var center = new Vec2(rng.Range(-590f, 590f), rng.Range(-560f, 590f));
                int ni = map.Nav.CellIndexAt(center);
                if (ni < 0) continue;
                bool outside = (map.Nav.Flags[ni] & NavFlags.OutOfBounds) != 0;
                if (!outside && map.RoadDist[ni] < 30f) continue;
                if (map.SampleHeight(center) < map.WaterLevel + 0.5f) continue;
                bool inZone = false;
                foreach (var z in l.Zones) if (Vec2.Distance(center, z.Center) < z.Radius * 0.9f) inZone = true;
                if (inZone) continue;
                int trees = rng.Range(7, 18);
                for (int t = 0; t < trees; t++)
                {
                    Vec2 p = center + rng.InsideCircle(22f);
                    float h = map.SampleHeight(p);
                    if (h > map.Config.BaseHeight + 70f) continue;
                    PropKind kind = (p.Z > 60f || h > map.Config.BaseHeight + 12f) ? PropKind.SnowPine : (rng.Chance(0.15f) ? PropKind.DeadTree : PropKind.PineTree);
                    Add(c, kind, p, rng.Range(0f, MathUtil.TwoPi), rng.Range(0.8f, 1.35f), 0f);
                }
                clusters++;
            }

            // Rocks on slopes / boundary.
            for (int i = 0; i < 260; i++)
            {
                var p = new Vec2(rng.Range(-595f, 595f), rng.Range(-590f, 595f));
                float h = map.SampleHeight(p);
                if (h < map.WaterLevel + 0.5f) continue;
                int ni = map.Nav.CellIndexAt(p);
                bool playable = ni >= 0 && (map.Nav.Flags[ni] & NavFlags.OutOfBounds) == 0;
                float s = rng.Range(0.7f, playable ? 1.6f : 3.5f);
                Add(c, rng.Chance(0.3f) ? PropKind.Boulder : PropKind.Rock, p, rng.Range(0f, MathUtil.TwoPi), s, playable ? 1.2f * s : 0f, rng.Range(0, 4));
            }

            // Ships in the southern bays.
            var shipSpots = new[] { new Vec2(-520f, -545f), new Vec2(-440f, -560f), new Vec2(530f, -540f), new Vec2(455f, -565f), new Vec2(585f, -420f), new Vec2(-585f, -430f) };
            foreach (var s in shipSpots)
                if (map.SampleHeight(s) < map.WaterLevel - 2f)
                    AddUnchecked(c, PropKind.Ship, s, rng.Range(0f, MathUtil.TwoPi), rng.Range(0.9f, 1.2f), 0f, rng.Range(0, 2));
        }

        private static void AddUncheckedIfDry(PropContext c, PropKind kind, Vec2 p, float yaw, float scale, float tilt)
        {
            if (c.Map.SampleHeight(p) < c.Map.WaterLevel + 0.3f) return;
            foreach (var b in c.Map.Layout.Bridges) if (b.Contains(p, 4f)) return;
            AddUnchecked(c, kind, p, yaw, scale, 0f, 0, tilt);
        }

        private static void PlaceBiome(PropContext c, ZoneDef z)
        {
            var rng = c.Rng;
            switch (z.Biome)
            {
                case "snow_fort":
                case "fort":
                case "wall":
                {
                    // Partial concrete perimeter with gaps.
                    int segs = 22;
                    for (int s = 0; s < segs; s++)
                    {
                        if (s % 4 == 0 || rng.Chance(0.25f)) continue;
                        float yaw = s / (float)segs * MathUtil.TwoPi;
                        Vec2 p = z.Center + Vec2.FromYaw(yaw) * (z.Radius * 0.82f);
                        Add(c, PropKind.ConcreteWall, p, yaw + MathUtil.Pi * 0.5f, 1f, 2.2f, rng.Range(0, 3));
                    }
                    Scatter(c, z, PropKind.Watchtower, 2, 0.55f, 0.75f, 1f, 1.1f, 2.2f);
                    Scatter(c, z, PropKind.BunkerRuin, 2, 0.4f, 0.7f, 0.9f, 1.2f, 3.6f);
                    Scatter(c, z, PropKind.Sandbags, 6, 0.3f, 0.75f, 1f, 1.4f, 0f);
                    Scatter(c, z, PropKind.TankTrap, 8, 0.85f, 1.15f, 0.9f, 1.2f, 0f);
                    Scatter(c, z, PropKind.SnowPine, 6, 1.0f, 1.3f, 0.8f, 1.3f, 0f);
                    break;
                }
                case "snow_mountain":
                    Scatter(c, z, PropKind.Boulder, 6, 0.45f, 1.1f, 1.2f, 2.4f, 1.4f);
                    Scatter(c, z, PropKind.SnowPine, 22, 0.5f, 1.3f, 0.8f, 1.4f, 0f);
                    Scatter(c, z, PropKind.Watchtower, 1, 0.5f, 0.7f, 1f, 1f, 2.2f);
                    Scatter(c, z, PropKind.Sandbags, 3, 0.3f, 0.6f, 1f, 1.2f, 0f);
                    break;
                case "industrial":
                    Scatter(c, z, PropKind.Factory, 3, 0.5f, 0.85f, 0.9f, 1.15f, 9f, 10);
                    Scatter(c, z, PropKind.Chimney, 3, 0.55f, 0.95f, 0.9f, 1.3f, 2.2f);
                    Scatter(c, z, PropKind.StorageTank, 3, 0.45f, 0.9f, 0.8f, 1.2f, 4.2f);
                    Scatter(c, z, PropKind.Crane, 1, 0.5f, 0.8f, 1f, 1f, 2f);
                    Scatter(c, z, PropKind.CrateStack, 6, 0.3f, 0.9f, 0.8f, 1.3f, 1.4f);
                    Scatter(c, z, PropKind.WreckedTruck, 2, 0.4f, 0.9f, 1f, 1f, 2.6f);
                    Scatter(c, z, PropKind.RubblePile, 5, 0.3f, 1f, 0.8f, 1.5f, 0f);
                    break;
                case "ruins":
                {
                    // City blocks in a loose grid leaving streets.
                    for (int gx = -2; gx <= 2; gx++)
                        for (int gz = -2; gz <= 2; gz++)
                        {
                            if (gx == 0 || gz == 0) continue; // main streets
                            Vec2 p = z.Center + new Vec2(gx * 24f + rng.Range(-4f, 4f), gz * 24f + rng.Range(-4f, 4f));
                            if (Vec2.Distance(p, z.Center) > z.Radius * 1.05f) continue;
                            Add(c, PropKind.RuinedBuilding, p, (rng.Range(0, 4)) * MathUtil.Pi * 0.5f + rng.Range(-0.1f, 0.1f), rng.Range(0.85f, 1.2f), 7f, rng.Range(0, 4));
                        }
                    Scatter(c, z, PropKind.RubblePile, 10, 0.1f, 1f, 0.8f, 1.6f, 0f);
                    Scatter(c, z, PropKind.BurntCar, 5, 0.2f, 0.9f, 1f, 1f, 1.8f);
                    Scatter(c, z, PropKind.Lamp, 6, 0.2f, 0.9f, 1f, 1f, 0f);
                    Scatter(c, z, PropKind.RuinedWall, 6, 0.4f, 1.05f, 0.9f, 1.3f, 2f);
                    break;
                }
                case "tank_graveyard":
                    Scatter(c, z, PropKind.WreckedTank, 14, 0.25f, 1.05f, 0.95f, 1.15f, 3f, 10);
                    Scatter(c, z, PropKind.WreckedHalftrack, 4, 0.3f, 1f, 1f, 1f, 2.6f);
                    Scatter(c, z, PropKind.TankTrap, 6, 0.6f, 1.1f, 0.9f, 1.2f, 0f);
                    Scatter(c, z, PropKind.SnowPine, 5, 1f, 1.3f, 0.8f, 1.2f, 0f);
                    break;
                case "trench":
                    Scatter(c, z, PropKind.Sandbags, 10, 0.15f, 0.95f, 1f, 1.4f, 0f);
                    Scatter(c, z, PropKind.BarbedWire, 8, 0.7f, 1.1f, 1f, 1.3f, 0f);
                    Scatter(c, z, PropKind.TrenchRevetment, 10, 0.1f, 0.85f, 1f, 1.2f, 0f);
                    Scatter(c, z, PropKind.BunkerRuin, 1, 0.5f, 0.8f, 1f, 1f, 3.6f);
                    Scatter(c, z, PropKind.DeadTree, 6, 0.5f, 1.2f, 0.9f, 1.2f, 0f);
                    break;
                case "rail":
                {
                    // Railway running east-west through the zone with derailed wagons.
                    float rz = z.Center.Z + 16f;
                    for (float x = z.Center.X - z.Radius * 1.25f; x <= z.Center.X + z.Radius * 1.25f; x += 12f)
                        AddUnchecked(c, PropKind.RailTrack, new Vec2(x, rz), MathUtil.Pi * 0.5f, 1f, 0f);
                    for (float x = z.Center.X - z.Radius * 1.25f; x <= z.Center.X + z.Radius * 1.25f; x += 12f)
                        AddUnchecked(c, PropKind.RailTrack, new Vec2(x, rz + 7f), MathUtil.Pi * 0.5f, 1f, 0f);
                    Add(c, PropKind.TrainCar, new Vec2(z.Center.X - 34f, rz), MathUtil.Pi * 0.5f, 1f, 3f, 0);
                    Add(c, PropKind.TrainCar, new Vec2(z.Center.X - 48f, rz + 7f), MathUtil.Pi * 0.5f + 0.25f, 1f, 3f, 1, 0.3f);
                    Add(c, PropKind.TrainCar, new Vec2(z.Center.X + 40f, rz + 2f), MathUtil.Pi * 0.5f - 0.5f, 1f, 3f, 2, -0.4f);
                    Add(c, PropKind.Station, z.Center + new Vec2(-30f, -26f), 0f, 1f, 7f);
                    Add(c, PropKind.WaterTower, z.Center + new Vec2(34f, -24f), 0f, 1f, 2.5f);
                    Scatter(c, z, PropKind.CrateStack, 5, 0.3f, 0.9f, 0.8f, 1.2f, 1.4f);
                    break;
                }
                case "highland":
                    Scatter(c, z, PropKind.Rock, 8, 0.4f, 1.1f, 0.8f, 1.8f, 1.2f);
                    Scatter(c, z, PropKind.SnowPine, 12, 0.6f, 1.25f, 0.8f, 1.3f, 0f);
                    Scatter(c, z, PropKind.ArtilleryRuin, 2, 0.35f, 0.7f, 1f, 1.1f, 2.6f);
                    Scatter(c, z, PropKind.Sandbags, 4, 0.3f, 0.8f, 1f, 1.3f, 0f);
                    break;
                case "strategic":
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float yaw = MathUtil.Pi * 0.25f + k * MathUtil.Pi * 0.5f;
                        Add(c, PropKind.Bunker, z.Center + Vec2.FromYaw(yaw) * (z.Radius * 0.62f), yaw + MathUtil.Pi, 1.1f, 4f);
                    }
                    for (int s = 0; s < 28; s++)
                    {
                        if (s % 7 == 0 || s % 7 == 1) continue;
                        float yaw = s / 28f * MathUtil.TwoPi;
                        Add(c, PropKind.ConcreteWall, z.Center + Vec2.FromYaw(yaw) * (z.Radius * 0.88f), yaw + MathUtil.Pi * 0.5f, 1f, 2.2f, rng.Range(0, 3));
                    }
                    AddUnchecked(c, PropKind.Flagpole, z.Center + new Vec2(0f, 22f), 0f, 1.4f, 0f);
                    Scatter(c, z, PropKind.Sandbags, 8, 0.25f, 0.8f, 1f, 1.4f, 0f);
                    Scatter(c, z, PropKind.TankTrap, 10, 0.9f, 1.2f, 0.9f, 1.2f, 0f);
                    Scatter(c, z, PropKind.WreckedTank, 3, 0.4f, 0.9f, 1f, 1f, 3f);
                    Scatter(c, z, PropKind.RubblePile, 6, 0.3f, 1f, 0.8f, 1.4f, 0f);
                    break;
                }
                case "outpost":
                    Scatter(c, z, PropKind.Tent, 4, 0.4f, 0.9f, 0.9f, 1.1f, 3.2f);
                    Scatter(c, z, PropKind.CrateStack, 5, 0.3f, 0.9f, 0.8f, 1.2f, 1.4f);
                    Scatter(c, z, PropKind.Sandbags, 6, 0.6f, 1f, 1f, 1.3f, 0f);
                    Scatter(c, z, PropKind.Watchtower, 1, 0.5f, 0.8f, 1f, 1f, 2.2f);
                    Scatter(c, z, PropKind.PineTree, 10, 0.9f, 1.3f, 0.8f, 1.3f, 0f);
                    break;
                case "plains":
                    Scatter(c, z, PropKind.WreckedTruck, 2, 0.4f, 1f, 1f, 1f, 2.6f);
                    Scatter(c, z, PropKind.WreckedHalftrack, 2, 0.4f, 1f, 1f, 1f, 2.6f);
                    Scatter(c, z, PropKind.BarbedWire, 6, 0.5f, 1.1f, 1f, 1.2f, 0f);
                    Scatter(c, z, PropKind.Sandbags, 4, 0.3f, 0.8f, 1f, 1.2f, 0f);
                    Scatter(c, z, PropKind.PineTree, 6, 1f, 1.3f, 0.8f, 1.2f, 0f);
                    break;
                case "swamp":
                    Scatter(c, z, PropKind.DeadTree, 22, 0.2f, 1.25f, 0.8f, 1.4f, 0f);
                    Scatter(c, z, PropKind.Reeds, 30, 0.2f, 1.25f, 0.8f, 1.3f, 0f);
                    Scatter(c, z, PropKind.WreckedHalftrack, 1, 0.5f, 0.9f, 1f, 1f, 0f);
                    break;
                case "forest":
                    Scatter(c, z, PropKind.PineTree, 46, 0.35f, 1.3f, 0.8f, 1.45f, 0f);
                    Scatter(c, z, PropKind.Rock, 6, 0.3f, 1.1f, 0.8f, 1.4f, 1.1f);
                    Scatter(c, z, PropKind.Sandbags, 3, 0.2f, 0.6f, 1f, 1.2f, 0f);
                    break;
                case "supply":
                    Scatter(c, z, PropKind.Tent, 4, 0.4f, 0.9f, 0.9f, 1.1f, 3.2f);
                    Scatter(c, z, PropKind.CrateStack, 10, 0.25f, 0.95f, 0.8f, 1.3f, 1.4f);
                    Scatter(c, z, PropKind.FuelDrums, 8, 0.25f, 0.95f, 0.9f, 1.1f, 0f);
                    Scatter(c, z, PropKind.ParkedTruck, 3, 0.4f, 0.9f, 1f, 1f, 2.6f);
                    Scatter(c, z, PropKind.Sandbags, 5, 0.7f, 1.05f, 1f, 1.3f, 0f);
                    break;
                case "coast":
                    Scatter(c, z, PropKind.TankTrap, 18, 0.5f, 1.3f, 0.9f, 1.2f, 0f);
                    Scatter(c, z, PropKind.Bunker, 1, 0.4f, 0.7f, 1f, 1f, 4f);
                    Scatter(c, z, PropKind.Sandbags, 5, 0.3f, 0.9f, 1f, 1.3f, 0f);
                    Scatter(c, z, PropKind.BarbedWire, 6, 0.6f, 1.2f, 1f, 1.2f, 0f);
                    break;
            }
            // A few craters' worth of debris and wreckage in every zone.
            Scatter(c, z, PropKind.RubblePile, 2, 0.4f, 1f, 0.7f, 1.2f, 0f);
        }

        // ------------------------------------------------------------------ lanes

        private static void BuildLanes(MapData map)
        {
            var pf = new Pathfinder(map.Nav);
            int idx = 0;
            foreach (var def in map.Layout.Lanes)
            {
                var raw = new List<Vec2>();
                Vec2 cur = def.Points[0];
                map.Nav.NearestWalkable(cur, false, 8, out cur);
                raw.Add(cur);
                for (int i = 1; i < def.Points.Count; i++)
                {
                    var seg = pf.FindPath(cur, def.Points[i], false);
                    if (seg == null || seg.Count == 0) continue;
                    raw.AddRange(seg);
                    cur = seg[seg.Count - 1];
                }
                var lane = new LanePath { Index = idx++, Def = def };
                Resample(raw, lane.Points, lane.Spacing);

                // Checkpoints: zones the lane passes through, in order from the player HQ.
                foreach (var z in map.Layout.Zones)
                {
                    int best = lane.NearestIndex(z.Center);
                    if (Vec2.Distance(lane.Points[best], z.Center) < z.Radius * 0.85f)
                        lane.Checkpoints.Add(new LaneCheckpoint { PathIndex = best, ZoneId = z.Id });
                }
                lane.Checkpoints.Sort((a, b) => a.PathIndex.CompareTo(b.PathIndex));
                map.Lanes.Add(lane);
            }
        }

        private static void Resample(List<Vec2> src, List<Vec2> dst, float spacing)
        {
            dst.Clear();
            if (src.Count == 0) return;
            dst.Add(src[0]);
            float carry = 0f;
            for (int i = 0; i < src.Count - 1; i++)
            {
                Vec2 a = src[i], b = src[i + 1];
                float len = Vec2.Distance(a, b);
                if (len < 1e-3f) continue;
                Vec2 dir = (b - a) / len;
                float t = spacing - carry;
                while (t <= len)
                {
                    dst.Add(a + dir * t);
                    t += spacing;
                }
                carry = len - (t - spacing);
            }
            if (Vec2.Distance(dst[dst.Count - 1], src[src.Count - 1]) > 0.5f) dst.Add(src[src.Count - 1]);
        }
    }
}
