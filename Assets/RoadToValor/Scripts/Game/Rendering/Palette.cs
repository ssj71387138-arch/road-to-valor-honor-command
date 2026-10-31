using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Shared colour atlas for all placeholder meshes. Every procedural model maps its vertices'
    /// UVs to a palette cell, so units, props and buildings share ONE material (cheap batching).
    /// Replace with real textured models later; nothing else depends on this.
    /// </summary>
    public static class Palette
    {
        public const int Grid = 8;          // 8x8 cells
        public const int CellPx = 4;        // pixels per cell
        private static Texture2D _tex;

        // Military palette (realistic, desaturated).
        public const int Uniform = 0, Helmet = 1, Webbing = 2, Leather = 3, Boot = 4, Skin = 5, Gunmetal = 6, Wood = 7;
        public const int TeamBlue = 8, TeamRed = 9, OfficerCoat = 10, NavyDark = 11, White = 12, Gold = 13, MedicRed = 14, GhillieStraw = 15;
        public const int GhillieDark = 16, ExoSteel = 17, Visor = 18, TankOlive = 19, TankGrey = 20, Track = 21, Rubber = 22, Concrete = 23;
        public const int ConcreteDark = 24, Sandbag = 25, Rust = 26, Burnt = 27, Snow = 28, PineGreen = 29, PineDark = 30, Bark = 31;
        public const int Rock = 32, RockDark = 33, Brick = 34, Roof = 35, Canvas = 36, Crate = 37, FuelDrum = 38, Glass = 39;
        public const int MetalLight = 40, Rail = 41, ShipHull = 42, ShipDeck = 43, FlagRed = 44, FlagBlue = 45, DressNavy = 46, DressRed = 47;
        public const int Black = 48, Hazard = 49, TeslaGlow = 50, Reed = 51, DeadWood = 52, Mud = 53, AirGrey = 54, AirDark = 55;
        public const int Canopy = 56, LampLight = 57, SteelDark = 58, CamoBrown = 59, Vest = 60, Pouch = 61, Bandage = 62, Rope = 63;

        private static readonly Color32[] Colors =
        {
            C(0.31f,0.34f,0.23f), C(0.22f,0.24f,0.17f), C(0.50f,0.46f,0.33f), C(0.32f,0.21f,0.13f), C(0.09f,0.08f,0.07f), C(0.76f,0.58f,0.46f), C(0.15f,0.15f,0.16f), C(0.40f,0.27f,0.15f),
            C(0.16f,0.40f,0.88f), C(0.84f,0.15f,0.12f), C(0.29f,0.31f,0.27f), C(0.11f,0.13f,0.19f), C(0.88f,0.88f,0.86f), C(0.86f,0.69f,0.26f), C(0.74f,0.10f,0.10f), C(0.50f,0.47f,0.30f),
            C(0.29f,0.30f,0.18f), C(0.37f,0.39f,0.41f), C(0.30f,0.90f,1.00f), C(0.30f,0.33f,0.22f), C(0.26f,0.27f,0.26f), C(0.11f,0.11f,0.11f), C(0.07f,0.07f,0.07f), C(0.55f,0.55f,0.53f),
            C(0.37f,0.37f,0.36f), C(0.58f,0.52f,0.38f), C(0.44f,0.24f,0.14f), C(0.11f,0.09f,0.08f), C(0.90f,0.92f,0.96f), C(0.13f,0.23f,0.15f), C(0.08f,0.15f,0.10f), C(0.24f,0.17f,0.11f),
            C(0.44f,0.44f,0.43f), C(0.29f,0.29f,0.29f), C(0.48f,0.29f,0.23f), C(0.21f,0.19f,0.19f), C(0.41f,0.41f,0.29f), C(0.49f,0.37f,0.21f), C(0.29f,0.34f,0.21f), C(0.14f,0.19f,0.24f),
            C(0.60f,0.61f,0.63f), C(0.34f,0.32f,0.31f), C(0.39f,0.41f,0.44f), C(0.34f,0.32f,0.29f), C(0.70f,0.10f,0.10f), C(0.10f,0.25f,0.70f), C(0.10f,0.12f,0.24f), C(0.54f,0.08f,0.08f),
            C(0.04f,0.04f,0.04f), C(0.84f,0.69f,0.10f), C(0.40f,0.72f,1.00f), C(0.44f,0.41f,0.24f), C(0.34f,0.31f,0.27f), C(0.29f,0.23f,0.16f), C(0.41f,0.44f,0.47f), C(0.24f,0.26f,0.29f),
            C(0.30f,0.40f,0.50f), C(1.00f,0.85f,0.50f), C(0.21f,0.22f,0.24f), C(0.35f,0.29f,0.19f), C(0.26f,0.28f,0.20f), C(0.36f,0.33f,0.22f), C(0.80f,0.78f,0.72f), C(0.45f,0.38f,0.26f),
        };

        private static Color32 C(float r, float g, float b) => new Color32((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), 255);

        public static Texture2D Texture
        {
            get
            {
                if (_tex != null) return _tex;
                int size = Grid * CellPx;
                _tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "RTV_Palette" };
                var px = new Color32[size * size];
                for (int i = 0; i < Colors.Length; i++)
                {
                    int cx = i % Grid, cy = i / Grid;
                    for (int y = 0; y < CellPx; y++)
                        for (int x = 0; x < CellPx; x++)
                            px[(cy * CellPx + y) * size + cx * CellPx + x] = Colors[i];
                }
                _tex.SetPixels32(px);
                _tex.Apply(false, false);
                return _tex;
            }
        }

        public static Vector2 UV(int index)
        {
            index = Mathf.Clamp(index, 0, Colors.Length - 1);
            float cx = index % Grid, cy = index / Grid;
            return new Vector2((cx + 0.5f) / Grid, (cy + 0.5f) / Grid);
        }

        public static Color Get(int index) => Colors[Mathf.Clamp(index, 0, Colors.Length - 1)];
        public static int Team(RoadToValor.Core.Team t) => t == RoadToValor.Core.Team.Enemy ? TeamRed : TeamBlue;
        public static int Flag(RoadToValor.Core.Team t) => t == RoadToValor.Core.Team.Enemy ? FlagRed : FlagBlue;
    }
}
