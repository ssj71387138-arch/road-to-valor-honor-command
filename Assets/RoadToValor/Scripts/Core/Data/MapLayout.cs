using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed class ZoneDef
    {
        public int Id;
        public string Name = "";
        public Team InitialOwner;
        public Vec2 Center;
        public float Radius;
        public string Biome = "";
        public Vec2[] PadPositions = new Vec2[0];
        public float PadYaw;
    }

    public sealed class HqDef
    {
        public Team Team;
        public string Name = "";
        public Vec2 Position;
        public float Radius;
    }

    public sealed class LaneDef
    {
        public string Name = "";
        public string Key = "";
        public List<Vec2> Points = new List<Vec2>();
    }

    public sealed class RiverDef
    {
        public float Width;
        public List<Vec2> Points = new List<Vec2>();
    }

    public sealed class BridgeDef
    {
        public Vec2 Center;
        public float Length;
        public float Width;
        /// <summary>Bridges run north-south across the east-west rivers.</summary>
        public bool Contains(Vec2 p, float margin = 0f)
            => Math.Abs(p.X - Center.X) <= Width * 0.5f + margin && Math.Abs(p.Z - Center.Z) <= Length * 0.5f + margin;
    }

    public sealed class CircleDef
    {
        public Vec2 Center;
        public float Radius;
        public float Height;
        public Team Team;
    }

    /// <summary>Battlefield layout loaded from map_layout.json (derived from the user's map concept).</summary>
    public sealed class MapLayout
    {
        public string Name = "";
        public readonly List<HqDef> Hqs = new List<HqDef>();
        public readonly List<ZoneDef> Zones = new List<ZoneDef>();
        public readonly List<LaneDef> Lanes = new List<LaneDef>();
        public readonly List<List<Vec2>> Trails = new List<List<Vec2>>();
        public readonly List<RiverDef> Rivers = new List<RiverDef>();
        public readonly List<BridgeDef> Bridges = new List<BridgeDef>();
        public readonly List<CircleDef> Fords = new List<CircleDef>();
        public readonly List<CircleDef> Hills = new List<CircleDef>();
        public readonly List<CircleDef> SupplyDepots = new List<CircleDef>();
        public float PlayableHalfWidth = 555f;
        public float NorthEdge = 570f;
        public float SouthEdge = -572f;
        public float MountainSouthLimit = -160f;

        public HqDef GetHq(Team t)
        {
            foreach (var h in Hqs) if (h.Team == t) return h;
            return null;
        }

        public static MapLayout Load(string json)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("map_layout.json root must be an object");
            var m = new MapLayout { Name = J.S(root, "name") };

            foreach (var h in J.Arr(root, "hqs") ?? new List<object>())
            {
                m.Hqs.Add(new HqDef
                {
                    Team = TeamUtil.Parse(J.S(h, "team")),
                    Name = J.S(h, "name"),
                    Position = new Vec2(J.F(h, "x"), J.F(h, "z")),
                    Radius = J.F(h, "radius", 60f),
                });
            }

            var padOffObj = J.Obj(root, "padOffset");
            float padX = J.F(padOffObj, "x", 24f), padZ = J.F(padOffObj, "z", 10f);

            foreach (var z in J.Arr(root, "zones") ?? new List<object>())
            {
                var zd = new ZoneDef
                {
                    Id = J.I(z, "id"),
                    Name = J.S(z, "name"),
                    InitialOwner = TeamUtil.Parse(J.S(z, "team")),
                    Center = new Vec2(J.F(z, "x"), J.F(z, "z")),
                    Radius = J.F(z, "radius", 60f),
                    Biome = J.S(z, "biome", "plains"),
                };
                var pads = J.Arr(z, "pads");
                if (pads != null && pads.Count > 0)
                {
                    zd.PadPositions = new Vec2[pads.Count];
                    for (int i = 0; i < pads.Count; i++) zd.PadPositions[i] = zd.Center + J.Point(pads[i]);
                }
                else
                {
                    // Default: two pads side by side, slightly toward the owner's rear.
                    float sideZ = zd.Center.Z >= 0 ? padZ : -padZ;
                    zd.PadPositions = new[]
                    {
                        new Vec2(zd.Center.X - padX, zd.Center.Z + sideZ),
                        new Vec2(zd.Center.X + padX, zd.Center.Z + sideZ),
                    };
                }
                m.Zones.Add(zd);
            }

            foreach (var l in J.Arr(root, "lanes") ?? new List<object>())
                m.Lanes.Add(new LaneDef { Name = J.S(l, "name"), Key = J.S(l, "key"), Points = J.Points(J.Arr(l, "points")) });

            foreach (var t in J.Arr(root, "trails") ?? new List<object>())
                m.Trails.Add(J.Points(t));

            foreach (var r in J.Arr(root, "rivers") ?? new List<object>())
                m.Rivers.Add(new RiverDef { Width = J.F(r, "width", 16f), Points = J.Points(J.Arr(r, "points")) });

            foreach (var b in J.Arr(root, "bridges") ?? new List<object>())
                m.Bridges.Add(new BridgeDef { Center = new Vec2(J.F(b, "x"), J.F(b, "z")), Length = J.F(b, "length", 40f), Width = J.F(b, "width", 10f) });

            foreach (var f in J.Arr(root, "fords") ?? new List<object>())
                m.Fords.Add(new CircleDef { Center = new Vec2(J.F(f, "x"), J.F(f, "z")), Radius = J.F(f, "radius", 10f) });

            foreach (var h in J.Arr(root, "hills") ?? new List<object>())
                m.Hills.Add(new CircleDef { Center = new Vec2(J.F(h, "x"), J.F(h, "z")), Radius = J.F(h, "radius", 30f), Height = J.F(h, "height", 25f) });

            foreach (var s in J.Arr(root, "supplyDepots") ?? new List<object>())
                m.SupplyDepots.Add(new CircleDef { Center = new Vec2(J.F(s, "x"), J.F(s, "z")), Radius = 20f, Team = TeamUtil.Parse(J.S(s, "team")) });

            var bnd = J.Obj(root, "boundary");
            m.PlayableHalfWidth = J.F(bnd, "playableHalfWidth", m.PlayableHalfWidth);
            m.NorthEdge = J.F(bnd, "northEdge", m.NorthEdge);
            m.SouthEdge = J.F(bnd, "southEdge", m.SouthEdge);
            m.MountainSouthLimit = J.F(bnd, "mountainSouthLimit", m.MountainSouthLimit);
            return m;
        }
    }
}
