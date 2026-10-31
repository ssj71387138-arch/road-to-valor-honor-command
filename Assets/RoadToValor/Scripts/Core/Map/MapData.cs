using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>Terrain texture layers. Order must match TerrainBuilder's TerrainLayer list.</summary>
    public enum SurfaceLayer { Snow = 0, Mud = 1, FrozenDirt = 2, Rock = 3, Swamp = 4, Road = 5, Concrete = 6, Sand = 7 }

    public enum PropKind
    {
        PineTree, SnowPine, DeadTree, Rock, Boulder,
        RuinedBuilding, RuinedWall, ConcreteWall, Bunker, BunkerRuin, Sandbags, BarbedWire, TankTrap,
        WreckedTank, WreckedTruck, WreckedHalftrack, BurntCar,
        Factory, Chimney, StorageTank, Crane, RailTrack, TrainCar, Station, WaterTower,
        Tent, CrateStack, FuelDrums, ParkedTruck, Watchtower, Ship, Flagpole, TrenchRevetment, Reeds, Lamp,
        TelegraphPole, RubblePile, ArtilleryRuin, HqWall, HqTower,
    }

    public struct PropInstance
    {
        public PropKind Kind;
        public Vec2 Pos;
        public float Yaw;
        public float Scale;
        public float BlockRadius;
        public int Variant;
        public float Tilt;
        public bool Snowy;
    }

    public struct LaneCheckpoint
    {
        public int PathIndex;
        public int ZoneId;
    }

    /// <summary>A lane resampled into evenly spaced points from the player HQ (index 0) to the enemy HQ.</summary>
    public sealed class LanePath
    {
        public int Index;
        public LaneDef Def;
        public readonly List<Vec2> Points = new List<Vec2>();
        public readonly List<LaneCheckpoint> Checkpoints = new List<LaneCheckpoint>();
        public float Spacing = 6f;

        public int Count => Points.Count;
        public Vec2 this[int i] => Points[MathUtil.Clamp(i, 0, Points.Count - 1)];

        public int NearestIndex(Vec2 p)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < Points.Count; i++)
            {
                float d = Vec2.DistanceSq(p, Points[i]);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public float DistanceTo(Vec2 p) => Vec2.Distance(p, Points[NearestIndex(p)]);
    }

    /// <summary>
    /// Generated battlefield: heightfield, navigation grid, prop placements and lane paths.
    /// Shared by the simulation (navigation) and the Unity layer (terrain, props, minimap).
    /// </summary>
    public sealed class MapData
    {
        public GameConfig Config;
        public MapLayout Layout;
        public float Size;
        public float Half;
        public int HeightRes;
        public float HeightCell;
        public float[] Heights;       // final heights (meters), row-major [z * res + x]
        public NavGrid Nav;
        public readonly List<PropInstance> Props = new List<PropInstance>();
        public readonly List<LanePath> Lanes = new List<LanePath>();
        public float[] RoadDist;      // nav resolution, distance (m) to lane roads
        public float[] TrailDist;     // nav resolution, distance (m) to jungle trails
        public readonly List<Vec2> Craters = new List<Vec2>();
        public readonly List<List<Vec2>> TrenchLines = new List<List<Vec2>>();
        public readonly Dictionary<int, float[]> HqGateYaws = new Dictionary<int, float[]>();
        public float WaterLevel => Config.WaterLevel;
        public float BridgeDeckHeight => Config.WaterLevel + 2.4f;

        public float SampleHeight(float x, float z)
        {
            float fx = (x + Half) / HeightCell, fz = (z + Half) / HeightCell;
            int x0 = (int)MathF.Floor(fx), z0 = (int)MathF.Floor(fz);
            x0 = MathUtil.Clamp(x0, 0, HeightRes - 2);
            z0 = MathUtil.Clamp(z0, 0, HeightRes - 2);
            float tx = MathUtil.Clamp01(fx - x0), tz = MathUtil.Clamp01(fz - z0);
            int i = z0 * HeightRes + x0;
            float a = Heights[i], b = Heights[i + 1], c = Heights[i + HeightRes], d = Heights[i + HeightRes + 1];
            float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
            return ab + (cd - ab) * tz;
        }

        public float SampleHeight(Vec2 p) => SampleHeight(p.X, p.Z);

        public BridgeDef BridgeAt(Vec2 p)
        {
            foreach (var b in Layout.Bridges) if (b.Contains(p, 0.5f)) return b;
            return null;
        }

        /// <summary>Height where a ground unit stands (bridge deck, wading in water, or terrain).</summary>
        public float GroundHeight(Vec2 p)
        {
            float h = SampleHeight(p);
            if (h < WaterLevel + 0.6f && BridgeAt(p) != null) return BridgeDeckHeight;
            if (h < WaterLevel - 0.7f) return WaterLevel - 0.7f;
            return h;
        }

        public bool InPlayableArea(Vec2 p)
        {
            int i = Nav.CellIndexAt(p);
            return i >= 0 && (Nav.Flags[i] & NavFlags.OutOfBounds) == 0;
        }

        public Vec2 ClampToMap(Vec2 p, float margin = 2f)
        {
            float h = Half - margin;
            return new Vec2(MathUtil.Clamp(p.X, -h, h), MathUtil.Clamp(p.Z, -h, h));
        }

        public float SampleField(float[] field, Vec2 p)
        {
            float fx = (p.X - Nav.Origin.X) / Nav.CellSize - 0.5f;
            float fz = (p.Z - Nav.Origin.Z) / Nav.CellSize - 0.5f;
            int x0 = MathUtil.Clamp((int)MathF.Floor(fx), 0, Nav.Width - 2);
            int z0 = MathUtil.Clamp((int)MathF.Floor(fz), 0, Nav.Height - 2);
            float tx = MathUtil.Clamp01(fx - x0), tz = MathUtil.Clamp01(fz - z0);
            int i = z0 * Nav.Width + x0;
            float a = field[i], b = field[i + 1], c = field[i + Nav.Width], d = field[i + Nav.Width + 1];
            float ab = a + (b - a) * tx, cd = c + (d - c) * tx;
            return ab + (cd - ab) * tz;
        }

        public ZoneDef ZoneAt(Vec2 p)
        {
            foreach (var z in Layout.Zones)
                if (Vec2.DistanceSq(p, z.Center) <= z.Radius * z.Radius) return z;
            return null;
        }

        /// <summary>
        /// Terrain texture weights for an alphamap of res x res texels and 8 layers, laid out [z][x][layer]
        /// exactly like Unity's TerrainData.SetAlphamaps array.
        /// </summary>
        public float[,,] ComputeSplat(int res)
        {
            const int L = 8;
            var result = new float[res, res, L];
            var w = new float[L];
            int seed = Config.MapSeed + 101;
            float texel = Size / res;
            for (int zi = 0; zi < res; zi++)
            {
                float z = -Half + (zi + 0.5f) * texel;
                for (int xi = 0; xi < res; xi++)
                {
                    float x = -Half + (xi + 0.5f) * texel;
                    var p = new Vec2(x, z);
                    Array.Clear(w, 0, L);
                    float h = SampleHeight(x, z);
                    float hx = SampleHeight(x + 2f, z) - SampleHeight(x - 2f, z);
                    float hz = SampleHeight(x, z + 2f) - SampleHeight(x, z - 2f);
                    float slope = MathF.Sqrt(hx * hx + hz * hz) / 4f;
                    float n1 = Noise.Fbm(x / 38f, z / 38f, seed, 3);
                    float n2 = Noise.Fbm(x / 11f, z / 11f, seed + 9, 2);

                    // Global gradients: snowy north + highlands, muddy war-torn centre, frozen dirt south.
                    float north = MathUtil.SmoothStep(-150f, 380f, z);
                    float center = 1f - MathUtil.SmoothStep(140f, 340f, Math.Abs(z));
                    w[(int)SurfaceLayer.Snow] = north * 0.9f + MathUtil.SmoothStep(20f, 34f, h) * 1.6f + MathUtil.SmoothStep(0.62f, 0.8f, n1) * 0.9f;
                    w[(int)SurfaceLayer.Mud] = center * (0.55f + n2 * 0.9f) + MathUtil.SmoothStep(0.55f, 0.75f, n2) * 0.25f;
                    w[(int)SurfaceLayer.FrozenDirt] = 0.35f + (1f - north) * 0.35f * n1;
                    w[(int)SurfaceLayer.Rock] = MathUtil.SmoothStep(0.42f, 0.85f, slope) * 3f + MathUtil.SmoothStep(45f, 70f, h) * 0.8f;

                    if (h < WaterLevel + 1.0f && h > WaterLevel - 2.5f)
                    {
                        bool coast = z < -380f || Math.Abs(x) > 520f;
                        if (coast) w[(int)SurfaceLayer.Sand] += 1.4f;
                        else w[(int)SurfaceLayer.Swamp] += 0.8f;
                    }

                    // Zone biomes.
                    foreach (var zone in Layout.Zones)
                    {
                        float d = Vec2.Distance(p, zone.Center);
                        if (d > zone.Radius * 1.3f) continue;
                        float infl = 1f - MathUtil.SmoothStep(zone.Radius * 0.65f, zone.Radius * 1.3f, d);
                        ApplyBiome(zone.Biome, infl, d / zone.Radius, n1, n2, w);
                    }
                    foreach (var hq in Layout.Hqs)
                    {
                        float d = Vec2.Distance(p, hq.Position);
                        if (d < hq.Radius * 1.25f)
                        {
                            float infl = 1f - MathUtil.SmoothStep(hq.Radius * 0.6f, hq.Radius * 1.25f, d);
                            w[(int)SurfaceLayer.Concrete] += infl * 2.2f;
                            w[(int)SurfaceLayer.Snow] *= 1f - infl * 0.6f;
                        }
                    }

                    // Build pads: concrete squares.
                    foreach (var zone in Layout.Zones)
                    {
                        foreach (var pad in zone.PadPositions)
                        {
                            float dx = Math.Abs(x - pad.X), dz = Math.Abs(z - pad.Z);
                            if (dx < 8f && dz < 8f) w[(int)SurfaceLayer.Concrete] += 6f * (1f - MathUtil.SmoothStep(6f, 8f, Math.Max(dx, dz)));
                        }
                    }

                    // Roads and trails.
                    float rd = SampleField(RoadDist, p);
                    float road = 1f - MathUtil.SmoothStep(4.5f, 8.5f, rd + (n2 - 0.5f) * 3f);
                    float td = SampleField(TrailDist, p);
                    float trail = (1f - MathUtil.SmoothStep(1.8f, 4.2f, td + (n2 - 0.5f) * 2f)) * 0.8f;
                    float r = Math.Max(road, trail);
                    if (r > 0f)
                    {
                        for (int k = 0; k < L; k++) w[k] *= 1f - r * 0.85f;
                        w[(int)SurfaceLayer.Road] += r * 3f;
                    }

                    float sum = 0f;
                    for (int k = 0; k < L; k++) { if (w[k] < 0f) w[k] = 0f; sum += w[k]; }
                    if (sum < 1e-4f) { w[(int)SurfaceLayer.FrozenDirt] = 1f; sum = 1f; }
                    for (int k = 0; k < L; k++) result[zi, xi, k] = w[k] / sum;
                }
            }
            return result;
        }

        private static void ApplyBiome(string biome, float infl, float dn, float n1, float n2, float[] w)
        {
            switch (biome)
            {
                case "snow_fort":
                    w[(int)SurfaceLayer.Snow] += infl * 0.8f;
                    w[(int)SurfaceLayer.Concrete] += infl * (dn < 0.45f ? 1.0f : 0.15f);
                    break;
                case "fort":
                case "wall":
                    w[(int)SurfaceLayer.Concrete] += infl * (dn < 0.5f ? 0.9f : 0.15f);
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.5f;
                    break;
                case "snow_mountain":
                    w[(int)SurfaceLayer.Snow] += infl * 1.2f;
                    w[(int)SurfaceLayer.Rock] += infl * 0.4f * n2;
                    break;
                case "industrial":
                    w[(int)SurfaceLayer.Concrete] += infl * 1.6f * (0.5f + n2);
                    w[(int)SurfaceLayer.Mud] += infl * 0.4f;
                    break;
                case "ruins":
                    w[(int)SurfaceLayer.Concrete] += infl * 1.2f;
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.6f * n2;
                    w[(int)SurfaceLayer.Snow] += infl * 0.3f;
                    break;
                case "tank_graveyard":
                    w[(int)SurfaceLayer.Mud] += infl * 1.2f;
                    w[(int)SurfaceLayer.Snow] += infl * 0.4f * n1;
                    break;
                case "trench":
                    w[(int)SurfaceLayer.Mud] += infl * 2.2f;
                    break;
                case "rail":
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.8f;
                    w[(int)SurfaceLayer.Concrete] += infl * 0.4f;
                    break;
                case "highland":
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.6f;
                    w[(int)SurfaceLayer.Snow] += infl * 0.7f * n1;
                    w[(int)SurfaceLayer.Rock] += infl * 0.25f;
                    break;
                case "strategic":
                    w[(int)SurfaceLayer.Mud] += infl * 1.2f;
                    w[(int)SurfaceLayer.Concrete] += infl * (dn < 0.55f ? 1.4f : 0.2f);
                    break;
                case "outpost":
                case "supply":
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.8f;
                    w[(int)SurfaceLayer.Mud] += infl * 0.5f;
                    break;
                case "plains":
                    w[(int)SurfaceLayer.Snow] += infl * 0.6f * n1;
                    w[(int)SurfaceLayer.Mud] += infl * 0.6f * n2;
                    break;
                case "swamp":
                    w[(int)SurfaceLayer.Swamp] += infl * 2.6f;
                    w[(int)SurfaceLayer.Mud] += infl * 0.6f;
                    break;
                case "forest":
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.6f;
                    w[(int)SurfaceLayer.Snow] += infl * 0.8f * n1;
                    break;
                case "coast":
                    w[(int)SurfaceLayer.Sand] += infl * 0.7f * n2;
                    w[(int)SurfaceLayer.FrozenDirt] += infl * 0.4f;
                    break;
            }
        }
    }
}
