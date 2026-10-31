using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    [Flags]
    public enum NavFlags : byte
    {
        None = 0,
        Walkable = 1,
        Water = 2,
        Bridge = 4,
        Ford = 8,
        Road = 16,
        Steep = 32,
        Obstacle = 64,
        OutOfBounds = 128,
    }

    /// <summary>
    /// Uniform navigation grid over the battlefield (default 4m cells => 300x300 for 1200m).
    /// Ground units path on it; air units ignore it.
    /// </summary>
    public sealed class NavGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float CellSize;
        public readonly Vec2 Origin; // world position of cell (0,0) corner
        public readonly NavFlags[] Flags;
        public readonly float[] Cost; // >= 1 : movement cost multiplier (mud, swamp, snow, ford)

        public NavGrid(int width, int height, float cellSize, Vec2 origin)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            Flags = new NavFlags[width * height];
            Cost = new float[width * height];
            for (int i = 0; i < Cost.Length; i++) Cost[i] = 1f;
        }

        public int Index(int cx, int cz) => cz * Width + cx;
        public bool InBounds(int cx, int cz) => cx >= 0 && cz >= 0 && cx < Width && cz < Height;

        public void WorldToCell(Vec2 p, out int cx, out int cz)
        {
            cx = (int)MathF.Floor((p.X - Origin.X) / CellSize);
            cz = (int)MathF.Floor((p.Z - Origin.Z) / CellSize);
        }

        public int CellIndexAt(Vec2 p)
        {
            WorldToCell(p, out int cx, out int cz);
            if (!InBounds(cx, cz)) return -1;
            return Index(cx, cz);
        }

        public Vec2 CellCenter(int cx, int cz) => new Vec2(Origin.X + (cx + 0.5f) * CellSize, Origin.Z + (cz + 0.5f) * CellSize);
        public Vec2 CellCenter(int index) => CellCenter(index % Width, index / Width);

        public bool IsWalkableIndex(int i, bool amphibious)
        {
            NavFlags f = Flags[i];
            if ((f & (NavFlags.Obstacle | NavFlags.OutOfBounds)) != 0) return false;
            if ((f & NavFlags.Bridge) != 0) return true;
            if ((f & NavFlags.Steep) != 0) return false;
            if ((f & NavFlags.Water) != 0) return amphibious || (f & NavFlags.Ford) != 0;
            return (f & NavFlags.Walkable) != 0;
        }

        public bool IsWalkable(int cx, int cz, bool amphibious) => InBounds(cx, cz) && IsWalkableIndex(Index(cx, cz), amphibious);

        public bool IsWalkable(Vec2 p, bool amphibious)
        {
            WorldToCell(p, out int cx, out int cz);
            return IsWalkable(cx, cz, amphibious);
        }

        public float CostAt(Vec2 p)
        {
            int i = CellIndexAt(p);
            return i < 0 ? 1f : Cost[i];
        }

        public bool Has(Vec2 p, NavFlags flag)
        {
            int i = CellIndexAt(p);
            return i >= 0 && (Flags[i] & flag) != 0;
        }

        /// <summary>Grid ray walk (Amanatides-Woo). True when every traversed cell is walkable.</summary>
        public bool LineWalkable(Vec2 a, Vec2 b, bool amphibious)
        {
            WorldToCell(a, out int x, out int z);
            WorldToCell(b, out int x1, out int z1);
            if (!IsWalkable(x, z, amphibious)) return false;
            float dx = b.X - a.X, dz = b.Z - a.Z;
            int stepX = dx > 0 ? 1 : (dx < 0 ? -1 : 0);
            int stepZ = dz > 0 ? 1 : (dz < 0 ? -1 : 0);
            float cellMinX = Origin.X + x * CellSize, cellMinZ = Origin.Z + z * CellSize;
            float tMaxX = stepX > 0 ? (cellMinX + CellSize - a.X) / dx : (stepX < 0 ? (cellMinX - a.X) / dx : float.MaxValue);
            float tMaxZ = stepZ > 0 ? (cellMinZ + CellSize - a.Z) / dz : (stepZ < 0 ? (cellMinZ - a.Z) / dz : float.MaxValue);
            float tDeltaX = stepX != 0 ? CellSize / Math.Abs(dx) : float.MaxValue;
            float tDeltaZ = stepZ != 0 ? CellSize / Math.Abs(dz) : float.MaxValue;
            int guard = Width + Height + 4;
            while ((x != x1 || z != z1) && guard-- > 0)
            {
                if (tMaxX < tMaxZ) { tMaxX += tDeltaX; x += stepX; }
                else { tMaxZ += tDeltaZ; z += stepZ; }
                if (!IsWalkable(x, z, amphibious)) return false;
            }
            return true;
        }

        /// <summary>Find the nearest walkable cell center (spiral search). Returns false if none within maxRadiusCells.</summary>
        public bool NearestWalkable(Vec2 p, bool amphibious, int maxRadiusCells, out Vec2 result)
        {
            WorldToCell(p, out int cx, out int cz);
            if (IsWalkable(cx, cz, amphibious)) { result = p; return true; }
            for (int r = 1; r <= maxRadiusCells; r++)
            {
                float best = float.MaxValue;
                int bx = -1, bz = -1;
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (!IsWalkable(x, z, amphibious)) continue;
                        float d = Vec2.DistanceSq(CellCenter(x, z), p);
                        if (d < best) { best = d; bx = x; bz = z; }
                    }
                }
                if (bx >= 0) { result = CellCenter(bx, bz); return true; }
            }
            result = p;
            return false;
        }
    }
}
