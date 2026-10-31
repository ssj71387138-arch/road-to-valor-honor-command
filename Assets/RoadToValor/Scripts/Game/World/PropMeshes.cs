using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>Placeholder geometry for every battlefield prop kind (ruins, trenches, wrecks, forests, factories...).</summary>
    public static class PropMeshes
    {
        private static readonly Dictionary<string, MeshBuilder> Cache = new Dictionary<string, MeshBuilder>();

        public static MeshBuilder Get(PropKind kind, int variant, bool snowy)
        {
            variant = Mathf.Abs(variant) % 4;
            string key = kind + "_" + variant + "_" + snowy;
            if (Cache.TryGetValue(key, out var mb)) return mb;
            mb = new MeshBuilder();
            Build(mb, kind, variant, snowy);
            Cache[key] = mb;
            return mb;
        }

        private static void SnowCap(MeshBuilder b, Vector3 c, Vector3 size)
        {
            b.Box(c + Vector3.up * (size.y * 0.5f + 0.06f), new Vector3(size.x * 1.02f, 0.14f, size.z * 1.02f), Palette.Snow);
        }

        private static void Build(MeshBuilder b, PropKind kind, int v, bool snowy)
        {
            switch (kind)
            {
                case PropKind.PineTree:
                case PropKind.SnowPine:
                {
                    bool snow = kind == PropKind.SnowPine;
                    b.Cylinder(new Vector3(0, 1.2f, 0), 0.28f, 2.4f, Palette.Bark, 6);
                    float[] r = { 2.4f, 1.9f, 1.35f, 0.8f };
                    float y = 1.6f;
                    for (int i = 0; i < 4; i++)
                    {
                        float h = 3.1f - i * 0.45f;
                        b.Cone(new Vector3(0, y, 0), r[i], h, i % 2 == 0 ? Palette.PineGreen : Palette.PineDark, 7);
                        if (snow) b.Cone(new Vector3(0, y + h * 0.35f, 0), r[i] * 0.72f, h * 0.68f, Palette.Snow, 7);
                        y += h * 0.62f;
                    }
                    break;
                }
                case PropKind.DeadTree:
                    b.Cylinder(new Vector3(0, 2.6f, 0), 0.25f, 5.2f, Palette.DeadWood, 6, default(Vector3), 0.12f);
                    b.Box(new Vector3(0.5f, 3.6f, 0), new Vector3(0.1f, 1.8f, 0.1f), Palette.DeadWood, new Vector3(0, 0, -40));
                    b.Box(new Vector3(-0.5f, 2.9f, 0.2f), new Vector3(0.1f, 1.5f, 0.1f), Palette.DeadWood, new Vector3(20, 0, 45));
                    b.Box(new Vector3(0.1f, 4.4f, -0.4f), new Vector3(0.08f, 1.2f, 0.08f), Palette.DeadWood, new Vector3(-35, 0, 0));
                    break;
                case PropKind.Rock:
                    b.Sphere(new Vector3(0, 0.35f, 0), new Vector3(2.2f, 1.4f, 1.8f), v % 2 == 0 ? Palette.Rock : Palette.RockDark, 4, 6);
                    b.Sphere(new Vector3(0.8f, 0.2f, 0.5f), new Vector3(1.1f, 0.8f, 1f), Palette.RockDark, 4, 5);
                    if (snowy) b.Sphere(new Vector3(0, 0.75f, 0), new Vector3(1.8f, 0.7f, 1.5f), Palette.Snow, 4, 6, true);
                    break;
                case PropKind.Boulder:
                    b.Sphere(new Vector3(0, 0.9f, 0), new Vector3(3.6f, 2.6f, 3.0f), Palette.Rock, 5, 7);
                    b.Sphere(new Vector3(-1.2f, 0.5f, 1f), new Vector3(1.8f, 1.4f, 1.6f), Palette.RockDark, 4, 6);
                    if (snowy) b.Sphere(new Vector3(0, 1.8f, 0), new Vector3(3.0f, 1.2f, 2.6f), Palette.Snow, 4, 7, true);
                    break;
                case PropKind.RuinedBuilding:
                {
                    float w = 9f + v, d = 8f + (v % 2) * 2f;
                    int wallCol = v % 2 == 0 ? Palette.Brick : Palette.Concrete;
                    float[] hs = { 7f - v, 4.5f, 8f, 3.5f + v };
                    b.Box(new Vector3(0, 0.2f, 0), new Vector3(w, 0.4f, d), Palette.ConcreteDark);
                    b.Box(new Vector3(0, hs[0] * 0.5f, d * 0.5f), new Vector3(w, hs[0], 0.5f), wallCol);
                    b.Box(new Vector3(-w * 0.25f, hs[1] * 0.5f, -d * 0.5f), new Vector3(w * 0.5f, hs[1], 0.5f), wallCol);
                    b.Box(new Vector3(w * 0.5f, hs[2] * 0.5f, 0), new Vector3(0.5f, hs[2], d), wallCol);
                    b.Box(new Vector3(-w * 0.5f, hs[3] * 0.5f, d * 0.15f), new Vector3(0.5f, hs[3], d * 0.7f), wallCol);
                    for (int i = 0; i < 3; i++)
                    {
                        b.Box(new Vector3(-w * 0.3f + i * w * 0.3f, 2.2f, d * 0.5f + 0.01f), new Vector3(1.1f, 1.5f, 0.52f), Palette.Black);
                        if (hs[0] > 5f) b.Box(new Vector3(-w * 0.3f + i * w * 0.3f, 5.2f, d * 0.5f + 0.01f), new Vector3(1.1f, 1.4f, 0.52f), Palette.Black);
                    }
                    b.Box(new Vector3(w * 0.1f, 3.6f, 0.4f), new Vector3(w * 0.7f, 0.3f, d * 0.5f), Palette.ConcreteDark, new Vector3(0, 0, 12)); // collapsed floor
                    for (int i = 0; i < 6; i++) b.Box(new Vector3(-w * 0.3f + i * 1.3f, 0.5f, -d * 0.3f + (i % 3) * 1.2f), new Vector3(1.4f, 0.9f, 1.1f), i % 2 == 0 ? wallCol : Palette.ConcreteDark, new Vector3(i * 17, i * 31, i * 7));
                    if (snowy) SnowCap(b, new Vector3(0, hs[0] * 0.5f, d * 0.5f), new Vector3(w, hs[0], 0.5f));
                    break;
                }
                case PropKind.RuinedWall:
                    b.Box(new Vector3(0, 1.5f, 0), new Vector3(6f, 3f, 0.5f), v % 2 == 0 ? Palette.Brick : Palette.Concrete);
                    b.Box(new Vector3(2.2f, 3.4f, 0), new Vector3(1.4f, 0.9f, 0.5f), v % 2 == 0 ? Palette.Brick : Palette.Concrete);
                    b.Box(new Vector3(-1f, 0.4f, 0.9f), new Vector3(1.4f, 0.8f, 1.2f), Palette.ConcreteDark, new Vector3(10, 30, 5));
                    break;
                case PropKind.ConcreteWall:
                case PropKind.HqWall:
                {
                    bool hq = kind == PropKind.HqWall;
                    float L = hq ? 6.8f : 4.6f, H = hq ? 4.2f : 2.4f, T = hq ? 1.6f : 0.8f;
                    b.Box(new Vector3(0, H * 0.5f, 0), new Vector3(L, H, T), Palette.Concrete);
                    b.Box(new Vector3(0, H + 0.1f, 0), new Vector3(L, 0.2f, T + 0.1f), Palette.ConcreteDark);
                    if (hq) for (int i = -2; i <= 2; i++) b.Box(new Vector3(i * 1.4f, H + 0.6f, 0), new Vector3(0.8f, 0.8f, T), Palette.Concrete);
                    if (!hq && v == 1) b.Box(new Vector3(1.4f, H - 0.4f, 0), new Vector3(1.2f, 1f, T + 0.05f), Palette.RockDark); // shell damage
                    if (snowy) b.Box(new Vector3(0, H + 0.28f, 0), new Vector3(L, 0.16f, T + 0.15f), Palette.Snow);
                    break;
                }
                case PropKind.HqTower:
                    b.Cylinder(new Vector3(0, 5f, 0), 2.6f, 10f, Palette.ConcreteDark, 8, default(Vector3), 2.3f);
                    b.Cylinder(new Vector3(0, 10.4f, 0), 3f, 0.8f, Palette.Concrete, 8);
                    for (int i = 0; i < 8; i++) { float a = i / 8f * Mathf.PI * 2f; b.Box(new Vector3(Mathf.Sin(a) * 2.7f, 11.2f, Mathf.Cos(a) * 2.7f), new Vector3(0.9f, 0.8f, 0.6f), Palette.Concrete, new Vector3(0, a * Mathf.Rad2Deg, 0)); }
                    b.Box(new Vector3(0, 6f, 2.5f), new Vector3(1f, 0.4f, 0.3f), Palette.Black);
                    if (snowy) b.Cylinder(new Vector3(0, 10.9f, 0), 2.9f, 0.18f, Palette.Snow, 8);
                    break;
                case PropKind.Bunker:
                    b.Wedge(new Vector3(0, 1.2f, 0), new Vector3(7f, 2.4f, 6f), 0.8f, 0.8f, 0f, Palette.Concrete);
                    b.Box(new Vector3(0, 1.4f, 2.95f), new Vector3(3f, 0.35f, 0.3f), Palette.Black);
                    b.Box(new Vector3(0, 0.4f, 3.6f), new Vector3(5f, 0.8f, 0.8f), Palette.Sandbag);
                    if (snowy) b.Box(new Vector3(0, 2.45f, 0), new Vector3(5.6f, 0.15f, 4.8f), Palette.Snow);
                    break;
                case PropKind.BunkerRuin:
                    b.Box(new Vector3(-1f, 0.9f, 0), new Vector3(4f, 1.8f, 5f), Palette.Concrete, new Vector3(0, 0, 8));
                    b.Box(new Vector3(2.2f, 0.6f, 0.5f), new Vector3(2.5f, 1.2f, 4f), Palette.ConcreteDark, new Vector3(12, 20, -15));
                    b.Box(new Vector3(0, 2.0f, -0.5f), new Vector3(5f, 0.4f, 4f), Palette.Concrete, new Vector3(0, 10, 14));
                    for (int i = 0; i < 4; i++) b.Box(new Vector3(-2f + i * 1.3f, 0.3f, 2.8f), new Vector3(1f, 0.6f, 0.8f), Palette.RockDark, new Vector3(i * 20, i * 40, 0));
                    if (snowy) b.Box(new Vector3(0, 2.25f, -0.5f), new Vector3(4.6f, 0.12f, 3.6f), Palette.Snow, new Vector3(0, 10, 14));
                    break;
                case PropKind.Sandbags:
                    for (int row = 0; row < 2; row++)
                        for (int i = 0; i < 5; i++)
                            b.Box(new Vector3(-1.9f + i * 0.95f + (row % 2) * 0.45f, 0.17f + row * 0.32f, 0), new Vector3(0.9f, 0.32f, 0.5f), Palette.Sandbag, new Vector3(0, (i * 7) % 10 - 5, 0));
                    if (snowy) b.Box(new Vector3(0, 0.7f, 0), new Vector3(4.2f, 0.08f, 0.5f), Palette.Snow);
                    break;
                case PropKind.BarbedWire:
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 p = new Vector3(-3f + i * 2f, 0.55f, 0);
                        b.Box(p, new Vector3(0.07f, 1.3f, 0.07f), Palette.Wood, new Vector3(0, 0, 35));
                        b.Box(p, new Vector3(0.07f, 1.3f, 0.07f), Palette.Wood, new Vector3(0, 0, -35));
                    }
                    for (int w = 0; w < 3; w++) b.Box(new Vector3(0, 0.3f + w * 0.28f, 0), new Vector3(6.4f, 0.025f, 0.025f), Palette.SteelDark);
                    break;
                case PropKind.TankTrap:
                    b.Box(new Vector3(0, 0.6f, 0), new Vector3(0.2f, 1.9f, 0.2f), Palette.Rust, new Vector3(0, 0, 55));
                    b.Box(new Vector3(0, 0.6f, 0), new Vector3(0.2f, 1.9f, 0.2f), Palette.Rust, new Vector3(0, 0, -55));
                    b.Box(new Vector3(0, 0.6f, 0), new Vector3(0.2f, 1.9f, 0.2f), Palette.Rust, new Vector3(55, 0, 0));
                    break;
                case PropKind.WreckedTank:
                {
                    int c = v % 2 == 0 ? Palette.Burnt : Palette.Rust;
                    b.Wedge(new Vector3(0, 0.9f, 0), new Vector3(2.6f, 1.1f, 6f), 1f, 0.82f, -0.04f, c);
                    b.Box(new Vector3(-1.45f, 0.55f, 0), new Vector3(0.6f, 1f, 6.2f), Palette.Track);
                    b.Box(new Vector3(1.45f, 0.45f, 0.4f), new Vector3(0.6f, 0.9f, 5.2f), Palette.Track, new Vector3(0, 0, 12));
                    if (v == 2) // turret blown off
                    {
                        b.Wedge(new Vector3(3.6f, 0.45f, 1.4f), new Vector3(2.2f, 0.75f, 2.6f), 0.8f, 0.78f, 0f, c, new Vector3(18, 40, 70));
                        b.Tube(new Vector3(4.6f, 0.3f, 3.2f), 0.09f, 3f, Palette.Gunmetal, 6, new Vector3(0, 30, 0));
                    }
                    else
                    {
                        b.Wedge(new Vector3(0.1f, 1.8f, -0.2f), new Vector3(2.2f, 0.75f, 2.6f), 0.8f, 0.78f, 0f, c, new Vector3(0, 35 + v * 20, 0));
                        b.Tube(new Vector3(1.2f, 1.85f, 1.4f), 0.09f, 3f, Palette.Gunmetal, 6, new Vector3(-8, 35 + v * 20, 0));
                    }
                    if (snowy) b.Box(new Vector3(0, 1.48f, 0), new Vector3(2.4f, 0.1f, 5.4f), Palette.Snow);
                    break;
                }
                case PropKind.WreckedTruck:
                case PropKind.ParkedTruck:
                {
                    bool wreck = kind == PropKind.WreckedTruck;
                    int c = wreck ? (v % 2 == 0 ? Palette.Burnt : Palette.Rust) : Palette.TankOlive;
                    b.Box(new Vector3(0, 0.7f, 0), new Vector3(2.2f, 0.4f, 6.4f), Palette.SteelDark);
                    b.Wedge(new Vector3(0, 1.5f, 2.3f), new Vector3(2.2f, 1.4f, 1.7f), 0.95f, 0.75f, -0.1f, c);
                    b.Box(new Vector3(0, 1.2f, -0.9f), new Vector3(2.3f, 0.6f, 4.2f), c);
                    if (!wreck) b.Wedge(new Vector3(0, 2.1f, -0.9f), new Vector3(2.3f, 1.2f, 4.2f), 0.8f, 1f, 0f, Palette.Canvas);
                    for (int i = 0; i < 3; i++) for (int s = -1; s <= 1; s += 2) b.Cylinder(new Vector3(s * 1.05f, 0.5f, 2.2f - i * 2.1f), 0.5f, 0.35f, Palette.Rubber, 8, new Vector3(0, 0, 90));
                    if (wreck && snowy) b.Box(new Vector3(0, 1.55f, -0.9f), new Vector3(2.2f, 0.1f, 4f), Palette.Snow);
                    break;
                }
                case PropKind.WreckedHalftrack:
                    b.Wedge(new Vector3(0, 1.0f, 0), new Vector3(2.3f, 1.3f, 6f), 0.9f, 0.9f, 0f, v % 2 == 0 ? Palette.Burnt : Palette.Rust);
                    b.Box(new Vector3(-1.2f, 0.5f, -1.2f), new Vector3(0.5f, 0.9f, 3.4f), Palette.Track);
                    b.Box(new Vector3(1.2f, 0.5f, -1.2f), new Vector3(0.5f, 0.9f, 3.4f), Palette.Track);
                    b.Cylinder(new Vector3(-1.1f, 0.45f, 2.2f), 0.45f, 0.3f, Palette.Rubber, 8, new Vector3(0, 0, 90));
                    break;
                case PropKind.BurntCar:
                    b.Box(new Vector3(0, 0.6f, 0), new Vector3(1.8f, 0.7f, 4.2f), Palette.Burnt);
                    b.Wedge(new Vector3(0, 1.2f, -0.2f), new Vector3(1.6f, 0.6f, 2.2f), 0.9f, 0.6f, 0f, Palette.Rust);
                    break;
                case PropKind.Factory:
                {
                    float w = 16f, d = 22f, h = 9f;
                    b.Box(new Vector3(0, h * 0.5f, 0), new Vector3(w, h, d), v % 2 == 0 ? Palette.Brick : Palette.Concrete);
                    for (int i = 0; i < 4; i++) b.Wedge(new Vector3(0, h + 1.2f, -d * 0.375f + i * d * 0.25f), new Vector3(w, 2.4f, d * 0.25f), 1f, 0.15f, 0.42f, Palette.Roof);
                    for (int i = 0; i < 5; i++)
                    {
                        b.Box(new Vector3(w * 0.5f + 0.02f, 4f, -d * 0.4f + i * d * 0.2f), new Vector3(0.1f, 2.6f, 2f), Palette.Glass);
                        b.Box(new Vector3(-w * 0.5f - 0.02f, 4f, -d * 0.4f + i * d * 0.2f), new Vector3(0.1f, 2.6f, 2f), Palette.Glass);
                    }
                    b.Box(new Vector3(0, 2.5f, d * 0.5f + 0.02f), new Vector3(5f, 5f, 0.1f), Palette.SteelDark);
                    b.Box(new Vector3(w * 0.3f, h + 3f, d * 0.3f), new Vector3(3f, 2f, 4f), Palette.RockDark, new Vector3(0, 0, 25)); // collapsed roof chunk
                    if (snowy) for (int i = 0; i < 4; i++) b.Wedge(new Vector3(0, h + 1.35f, -d * 0.375f + i * d * 0.25f), new Vector3(w * 0.98f, 2.3f, d * 0.2f), 1f, 0.12f, 0.42f, Palette.Snow);
                    break;
                }
                case PropKind.Chimney:
                    b.Cylinder(new Vector3(0, 9f, 0), 1.4f, 18f, Palette.Brick, 10, default(Vector3), 1.0f);
                    b.Cylinder(new Vector3(0, 18.2f, 0), 1.15f, 0.5f, Palette.Burnt, 10);
                    break;
                case PropKind.StorageTank:
                    b.Cylinder(new Vector3(0, 3f, 0), 4f, 6f, Palette.MetalLight, 14);
                    b.Sphere(new Vector3(0, 6f, 0), new Vector3(8f, 2.4f, 8f), Palette.MetalLight, 6, 14, true);
                    b.Box(new Vector3(4.05f, 3f, 0), new Vector3(0.1f, 6f, 0.5f), Palette.Rust);
                    if (snowy) b.Sphere(new Vector3(0, 6.4f, 0), new Vector3(7f, 1.8f, 7f), Palette.Snow, 6, 14, true);
                    break;
                case PropKind.Crane:
                    for (int i = 0; i < 4; i++) b.Box(new Vector3((i % 2 - 0.5f) * 1.6f, 8f, (i / 2 - 0.5f) * 1.6f), new Vector3(0.25f, 16f, 0.25f), Palette.Hazard);
                    for (int j = 1; j < 8; j++) b.Box(new Vector3(0, j * 2f, 0), new Vector3(1.8f, 0.15f, 1.8f), Palette.Hazard);
                    b.Box(new Vector3(0, 16.5f, 4f), new Vector3(0.6f, 0.6f, 14f), Palette.Hazard);
                    b.Box(new Vector3(0, 13f, 9f), new Vector3(0.04f, 7f, 0.04f), Palette.Black);
                    break;
                case PropKind.RailTrack:
                    b.Box(new Vector3(-0.75f, 0.12f, 0), new Vector3(0.1f, 0.15f, 12f), Palette.Rail);
                    b.Box(new Vector3(0.75f, 0.12f, 0), new Vector3(0.1f, 0.15f, 12f), Palette.Rail);
                    for (int i = 0; i < 15; i++) b.Box(new Vector3(0, 0.04f, -5.6f + i * 0.8f), new Vector3(2.4f, 0.1f, 0.25f), Palette.Wood);
                    break;
                case PropKind.TrainCar:
                    b.Box(new Vector3(0, 0.7f, 0), new Vector3(2.6f, 0.4f, 12f), Palette.SteelDark);
                    for (int i = -1; i <= 1; i += 2) for (int s = -1; s <= 1; s += 2) b.Cylinder(new Vector3(s * 0.85f, 0.45f, i * 4.5f), 0.45f, 0.2f, Palette.Black, 8, new Vector3(0, 0, 90));
                    if (v == 1) { b.Cylinder(new Vector3(0, 2.2f, 0), 1.5f, 11f, Palette.Black, 10, new Vector3(90, 0, 0)); }
                    else if (v == 2) { b.Box(new Vector3(0, 1.3f, 0), new Vector3(2.8f, 0.3f, 12f), Palette.Wood); b.Box(new Vector3(0, 2.2f, -2f), new Vector3(2.2f, 1.6f, 3f), Palette.Crate); b.Box(new Vector3(0, 2.2f, 2.5f), new Vector3(2f, 1.6f, 2.2f), Palette.Canvas); }
                    else { b.Box(new Vector3(0, 2.4f, 0), new Vector3(2.9f, 3f, 12f), Palette.Rust); b.Box(new Vector3(1.46f, 2.2f, 0), new Vector3(0.05f, 2.2f, 2.4f), Palette.Black); }
                    if (snowy) b.Box(new Vector3(0, 4.0f, 0), new Vector3(2.8f, 0.12f, 11.6f), Palette.Snow);
                    break;
                case PropKind.Station:
                    b.Box(new Vector3(0, 3f, 0), new Vector3(16f, 6f, 9f), Palette.Brick);
                    b.Wedge(new Vector3(0, 7f, 0), new Vector3(17f, 2f, 10f), 1f, 0.2f, 0f, Palette.Roof);
                    for (int i = -3; i <= 3; i++) b.Box(new Vector3(i * 2.1f, 3f, 4.52f), new Vector3(1.2f, 2.2f, 0.05f), Palette.Glass);
                    b.Box(new Vector3(0, 0.4f, 7f), new Vector3(18f, 0.8f, 4f), Palette.Concrete);
                    b.Box(new Vector3(6f, 5f, 2f), new Vector3(4f, 3f, 5f), Palette.RockDark, new Vector3(0, 0, -20)); // shell hole debris
                    if (snowy) b.Wedge(new Vector3(0, 7.2f, 0), new Vector3(16.5f, 1.9f, 9.5f), 1f, 0.2f, 0f, Palette.Snow);
                    break;
                case PropKind.WaterTower:
                    for (int i = 0; i < 4; i++) b.Box(new Vector3((i % 2 - 0.5f) * 3f, 4f, (i / 2 - 0.5f) * 3f), new Vector3(0.3f, 8f, 0.3f), Palette.Wood);
                    b.Cylinder(new Vector3(0, 10f, 0), 2.6f, 4f, Palette.Wood, 12);
                    b.Cone(new Vector3(0, 12f, 0), 2.8f, 1.4f, Palette.Roof, 12);
                    break;
                case PropKind.Tent:
                    b.Wedge(new Vector3(0, 1.3f, 0), new Vector3(4.2f, 2.6f, 5.5f), 0.08f, 1f, 0f, Palette.Canvas);
                    b.Box(new Vector3(0, 0.9f, 2.76f), new Vector3(1.2f, 1.6f, 0.02f), Palette.Black);
                    if (snowy) b.Wedge(new Vector3(0, 2.0f, 0), new Vector3(3f, 1.3f, 5.6f), 0.08f, 1f, 0f, Palette.Snow);
                    break;
                case PropKind.CrateStack:
                    b.Box(new Vector3(0, 0.5f, 0), new Vector3(1.2f, 1f, 1.2f), Palette.Crate);
                    b.Box(new Vector3(1.3f, 0.5f, 0.1f), new Vector3(1.2f, 1f, 1.2f), Palette.Crate, new Vector3(0, 12, 0));
                    if (v > 0) b.Box(new Vector3(0.6f, 1.5f, 0f), new Vector3(1.2f, 1f, 1.2f), Palette.TankOlive, new Vector3(0, 30, 0));
                    if (v > 1) b.Box(new Vector3(-1.2f, 0.4f, 0.6f), new Vector3(0.8f, 0.8f, 1.4f), Palette.TankOlive);
                    break;
                case PropKind.FuelDrums:
                    for (int i = 0; i < 5; i++) b.Cylinder(new Vector3((i % 3) * 0.65f, 0.45f, (i / 3) * 0.65f), 0.3f, 0.9f, i % 2 == 0 ? Palette.FuelDrum : Palette.Rust, 8);
                    break;
                case PropKind.Watchtower:
                    for (int i = 0; i < 4; i++) b.Box(new Vector3((i % 2 - 0.5f) * 2.4f, 3.5f, (i / 2 - 0.5f) * 2.4f), new Vector3(0.22f, 7f, 0.22f), Palette.Wood, new Vector3((i / 2 - 0.5f) * -6f, 0, (i % 2 - 0.5f) * 6f));
                    b.Box(new Vector3(0, 7f, 0), new Vector3(3.2f, 0.2f, 3.2f), Palette.Wood);
                    b.Box(new Vector3(0, 7.6f, 1.55f), new Vector3(3.2f, 1f, 0.1f), Palette.Wood);
                    b.Box(new Vector3(0, 7.6f, -1.55f), new Vector3(3.2f, 1f, 0.1f), Palette.Wood);
                    b.Wedge(new Vector3(0, 9.4f, 0), new Vector3(3.6f, 1.2f, 3.6f), 0.1f, 0.1f, 0f, Palette.Roof);
                    if (snowy) b.Wedge(new Vector3(0, 9.6f, 0), new Vector3(3.2f, 1.0f, 3.2f), 0.1f, 0.1f, 0f, Palette.Snow);
                    break;
                case PropKind.Ship:
                    b.Wedge(new Vector3(0, 1.5f, 0), new Vector3(9f, 5f, 48f), 1.15f, 1.05f, 0f, Palette.ShipHull);
                    b.Wedge(new Vector3(0, 2.5f, 24f), new Vector3(9f, 3f, 6f), 0.2f, 0.2f, 0.4f, Palette.ShipHull);
                    b.Box(new Vector3(0, 4.1f, 0), new Vector3(9.6f, 0.2f, 46f), Palette.ShipDeck);
                    b.Box(new Vector3(0, 7f, -3f), new Vector3(6f, 6f, 10f), Palette.ShipHull);
                    b.Box(new Vector3(0, 10.8f, -2f), new Vector3(4f, 2f, 6f), Palette.ShipHull);
                    b.Cylinder(new Vector3(0, 12f, -7f), 1.2f, 5f, Palette.Black, 8);
                    b.Box(new Vector3(0, 14f, 0f), new Vector3(0.3f, 8f, 0.3f), Palette.Gunmetal);
                    for (int i = -1; i <= 1; i += 2) { b.Cylinder(new Vector3(0, 5f, i * 15f), 1.8f, 1.2f, Palette.ShipHull, 10); b.Tube(new Vector3(0, 5.2f, i * 15f + i * 3f), 0.2f, 5f, Palette.Gunmetal); }
                    break;
                case PropKind.Flagpole:
                    b.Box(new Vector3(0, 7f, 0), new Vector3(0.25f, 14f, 0.25f), Palette.Gunmetal);
                    b.Plane(new Vector3(1.7f, 12.6f, 0), new Vector2(2.2f, 3.4f), Palette.FlagRed, new Vector3(0, 0, 90));
                    b.Box(new Vector3(0, 0.4f, 0), new Vector3(2f, 0.8f, 2f), Palette.Concrete);
                    break;
                case PropKind.TrenchRevetment:
                    b.Box(new Vector3(0, 0.4f, 0), new Vector3(3.2f, 0.9f, 0.12f), Palette.Wood);
                    for (int i = -1; i <= 1; i++) b.Box(new Vector3(i * 1.4f, 0.5f, 0.1f), new Vector3(0.12f, 1.2f, 0.12f), Palette.Wood);
                    break;
                case PropKind.Reeds:
                    for (int i = 0; i < 9; i++) b.Box(new Vector3((i % 3 - 1) * 0.35f, 0.8f, (i / 3 - 1) * 0.35f), new Vector3(0.05f, 1.6f + (i % 4) * 0.2f, 0.05f), Palette.Reed, new Vector3((i % 3 - 1) * 8f, 0, (i / 3 - 1) * 8f));
                    break;
                case PropKind.Lamp:
                    b.Box(new Vector3(0, 2.5f, 0), new Vector3(0.14f, 5f, 0.14f), Palette.SteelDark);
                    b.Box(new Vector3(0.5f, 4.9f, 0), new Vector3(1f, 0.1f, 0.1f), Palette.SteelDark);
                    b.Box(new Vector3(0.95f, 4.75f, 0), new Vector3(0.3f, 0.2f, 0.3f), Palette.LampLight);
                    break;
                case PropKind.TelegraphPole:
                    b.Box(new Vector3(0, 3.6f, 0), new Vector3(0.22f, 7.2f, 0.22f), Palette.Wood);
                    b.Box(new Vector3(0, 6.8f, 0), new Vector3(1.8f, 0.14f, 0.14f), Palette.Wood);
                    break;
                case PropKind.RubblePile:
                    for (int i = 0; i < 7; i++)
                        b.Box(new Vector3((i % 3 - 1) * 0.9f, 0.3f + (i / 3) * 0.25f, ((i * 7) % 3 - 1) * 0.8f), new Vector3(1f, 0.6f, 0.8f), i % 3 == 0 ? Palette.Brick : (i % 3 == 1 ? Palette.Concrete : Palette.RockDark), new Vector3(i * 23, i * 41, i * 11));
                    if (snowy) b.Sphere(new Vector3(0, 0.6f, 0), new Vector3(2.4f, 0.6f, 2f), Palette.Snow, 4, 6, true);
                    break;
                case PropKind.ArtilleryRuin:
                    for (int i = 0; i < 8; i++) { float a = i / 8f * Mathf.PI * 1.6f - 0.8f; b.Box(new Vector3(Mathf.Sin(a) * 3f, 0.35f, Mathf.Cos(a) * 3f), new Vector3(1.5f, 0.7f, 0.55f), Palette.Sandbag, new Vector3(0, a * Mathf.Rad2Deg, 0)); }
                    b.Tube(new Vector3(0, 0.9f, 0.8f), 0.12f, 3.4f, Palette.Gunmetal, 8, new Vector3(-12, 20, 0));
                    b.Box(new Vector3(0, 0.6f, -0.6f), new Vector3(1.6f, 0.8f, 1.4f), Palette.TankOlive, new Vector3(0, 20, 10));
                    b.Cylinder(new Vector3(-1f, 0.6f, -0.6f), 0.6f, 0.2f, Palette.Wood, 10, new Vector3(0, 0, 90));
                    break;
            }
        }

        // Bridge geometry in local space: deck along Z.
        public static MeshBuilder Bridge(float length, float width, float deckY, float bedY, bool wide)
        {
            var b = new MeshBuilder();
            b.Box(new Vector3(0, deckY - 0.4f, 0), new Vector3(width, 0.8f, length), wide ? Palette.Concrete : Palette.Wood);
            b.Box(new Vector3(0, deckY + 0.02f, 0), new Vector3(width * 0.8f, 0.06f, length), wide ? Palette.ConcreteDark : Palette.Wood);
            for (int s = -1; s <= 1; s += 2)
            {
                b.Box(new Vector3(s * width * 0.5f, deckY + 0.6f, 0), new Vector3(0.25f, 0.2f, length), wide ? Palette.Concrete : Palette.Wood);
                for (float z = -length * 0.5f; z <= length * 0.5f; z += 3f)
                    b.Box(new Vector3(s * width * 0.5f, deckY + 0.35f, z), new Vector3(0.2f, 0.7f, 0.2f), wide ? Palette.ConcreteDark : Palette.Wood);
            }
            float pierH = deckY - bedY + 0.5f;
            for (int i = -1; i <= 1; i++)
                b.Box(new Vector3(0, bedY + pierH * 0.5f - 0.8f, i * length * 0.25f), new Vector3(width * 0.7f, pierH, 1.4f), wide ? Palette.ConcreteDark : Palette.Wood);
            // damaged section + sandbag checkpoint
            b.Box(new Vector3(width * 0.3f, deckY + 0.3f, length * 0.42f), new Vector3(1.8f, 0.6f, 0.6f), Palette.Sandbag);
            return b;
        }
    }
}
