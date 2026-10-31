using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>
    /// Uniform-grid spatial partition rebuilt every tick. Turns "find enemies in range" from O(n^2)
    /// into a handful of cell lookups, which keeps 300+ units cheap on the 1200m map.
    /// </summary>
    public sealed class SpatialHash
    {
        private readonly float _cell;
        private readonly int _dim;
        private readonly float _origin;
        private readonly List<SimUnit>[] _cells;
        private readonly List<int> _usedCells = new List<int>();

        public SpatialHash(float worldSize, float cellSize)
        {
            _cell = cellSize;
            _dim = (int)Math.Ceiling(worldSize / cellSize) + 1;
            _origin = -worldSize * 0.5f;
            _cells = new List<SimUnit>[_dim * _dim];
            for (int i = 0; i < _cells.Length; i++) _cells[i] = new List<SimUnit>(8);
        }

        private int CellCoord(float v) => Math.Max(0, Math.Min(_dim - 1, (int)((v - _origin) / _cell)));

        public void Clear()
        {
            foreach (int i in _usedCells) _cells[i].Clear();
            _usedCells.Clear();
        }

        public void Insert(SimUnit u)
        {
            int i = CellCoord(u.Pos.Z) * _dim + CellCoord(u.Pos.X);
            var list = _cells[i];
            if (list.Count == 0) _usedCells.Add(i);
            list.Add(u);
        }

        /// <summary>Appends alive units within radius to results (results is NOT cleared).</summary>
        public void Query(Vec2 center, float radius, List<SimUnit> results)
        {
            int x0 = CellCoord(center.X - radius), x1 = CellCoord(center.X + radius);
            int z0 = CellCoord(center.Z - radius), z1 = CellCoord(center.Z + radius);
            float r2 = radius * radius;
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var list = _cells[z * _dim + x];
                    for (int k = 0; k < list.Count; k++)
                    {
                        var u = list[k];
                        if (!u.Alive) continue;
                        if (Vec2.DistanceSq(u.Pos, center) <= r2) results.Add(u);
                    }
                }
            }
        }

        public int Count(Vec2 center, float radius, Team team, bool groundOnly)
        {
            int x0 = CellCoord(center.X - radius), x1 = CellCoord(center.X + radius);
            int z0 = CellCoord(center.Z - radius), z1 = CellCoord(center.Z + radius);
            float r2 = radius * radius;
            int n = 0;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    var list = _cells[z * _dim + x];
                    for (int k = 0; k < list.Count; k++)
                    {
                        var u = list[k];
                        if (!u.Alive || u.Team != team) continue;
                        if (groundOnly && !u.IsGround) continue;
                        if (Vec2.DistanceSq(u.Pos, center) <= r2) n++;
                    }
                }
            return n;
        }
    }
}
