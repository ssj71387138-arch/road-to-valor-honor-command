using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>
    /// Grid A* (8-neighbour, no corner cutting, octile heuristic, terrain cost) with string-pulling smoothing.
    /// Allocation free after construction: node arrays are reused with a generation stamp.
    /// </summary>
    public sealed class Pathfinder
    {
        private readonly NavGrid _grid;
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private readonly int[] _closedStamp;
        private int _gen;
        private readonly MinHeap _open;

        public int LastExpanded { get; private set; }
        public int MaxExpansions = 60000;

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float Diag = 1.41421356f;

        public Pathfinder(NavGrid grid)
        {
            _grid = grid;
            int n = grid.Width * grid.Height;
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
            _closedStamp = new int[n];
            _open = new MinHeap(4096);
        }

        /// <summary>Returns a smoothed world-space path (excluding start, including goal) or null if unreachable.</summary>
        public List<Vec2> FindPath(Vec2 start, Vec2 goal, bool amphibious)
        {
            var grid = _grid;
            if (!grid.NearestWalkable(start, amphibious, 6, out Vec2 s)) return null;
            if (!grid.NearestWalkable(goal, amphibious, 12, out Vec2 g)) return null;
            goal = g;

            if (grid.LineWalkable(s, goal, amphibious))
                return new List<Vec2> { goal };

            grid.WorldToCell(s, out int sx, out int sz);
            grid.WorldToCell(goal, out int gx, out int gz);
            int startIdx = grid.Index(sx, sz), goalIdx = grid.Index(gx, gz);

            unchecked { _gen++; }
            if (_gen == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); Array.Clear(_closedStamp, 0, _closedStamp.Length); _gen = 1; }
            _open.Clear();
            _g[startIdx] = 0f;
            _parent[startIdx] = -1;
            _stamp[startIdx] = _gen;
            _open.Push(startIdx, Heuristic(sx, sz, gx, gz));

            int expanded = 0;
            int found = -1;
            int bestIdx = startIdx;
            float bestH = float.MaxValue;
            while (_open.Count > 0)
            {
                int cur = _open.Pop();
                if (_closedStamp[cur] == _gen) continue;
                _closedStamp[cur] = _gen;
                if (cur == goalIdx) { found = cur; break; }
                if (++expanded > MaxExpansions) break;

                int cx = cur % grid.Width, cz = cur / grid.Width;
                float h = Heuristic(cx, cz, gx, gz);
                if (h < bestH) { bestH = h; bestIdx = cur; }

                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + Dx[k], nz = cz + Dz[k];
                    if (!grid.InBounds(nx, nz)) continue;
                    int ni = grid.Index(nx, nz);
                    if (_closedStamp[ni] == _gen) continue;
                    if (!grid.IsWalkableIndex(ni, amphibious)) continue;
                    if (k >= 4)
                    {
                        // no corner cutting
                        if (!grid.IsWalkable(cx + Dx[k], cz, amphibious) || !grid.IsWalkable(cx, cz + Dz[k], amphibious)) continue;
                    }
                    float step = (k >= 4 ? Diag : 1f) * grid.Cost[ni];
                    float ng = _g[cur] + step;
                    if (_stamp[ni] != _gen || ng < _g[ni])
                    {
                        _stamp[ni] = _gen;
                        _g[ni] = ng;
                        _parent[ni] = cur;
                        _open.Push(ni, ng + Heuristic(nx, nz, gx, gz));
                    }
                }
            }
            LastExpanded = expanded;

            // Unreachable goal: walk to the closest explored cell instead (units still try to approach).
            if (found < 0) found = bestIdx;
            if (found == startIdx) return null;

            var cells = new List<int>();
            for (int c = found; c != -1 && c != startIdx; c = _parent[c]) cells.Add(c);
            cells.Reverse();

            var raw = new List<Vec2>(cells.Count + 1);
            foreach (int c in cells) raw.Add(grid.CellCenter(c));
            if (found == goalIdx) raw[raw.Count - 1] = goal;
            return Smooth(s, raw, amphibious);
        }

        private List<Vec2> Smooth(Vec2 start, List<Vec2> raw, bool amphibious)
        {
            var result = new List<Vec2>();
            Vec2 anchor = start;
            int i = 0;
            while (i < raw.Count)
            {
                int far = i;
                // Greedy: the furthest point visible from the anchor (bounded look-ahead for speed).
                int limit = Math.Min(raw.Count - 1, i + 40);
                for (int j = limit; j > i; j--)
                {
                    if (_grid.LineWalkable(anchor, raw[j], amphibious)) { far = j; break; }
                }
                result.Add(raw[far]);
                anchor = raw[far];
                i = far + 1;
            }
            return result;
        }

        private static float Heuristic(int x0, int z0, int x1, int z1)
        {
            int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
            int mn = Math.Min(dx, dz), mx = Math.Max(dx, dz);
            return mn * Diag + (mx - mn);
        }

        private sealed class MinHeap
        {
            private int[] _items;
            private float[] _keys;
            public int Count;

            public MinHeap(int cap) { _items = new int[cap]; _keys = new float[cap]; }
            public void Clear() { Count = 0; }

            public void Push(int item, float key)
            {
                if (Count == _items.Length)
                {
                    Array.Resize(ref _items, Count * 2);
                    Array.Resize(ref _keys, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p];
                    _keys[i] = _keys[p];
                    i = p;
                }
                _items[i] = item;
                _keys[i] = key;
            }

            public int Pop()
            {
                int top = _items[0];
                int lastItem = _items[--Count];
                float lastKey = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= Count) break;
                    int r = l + 1;
                    int m = (r < Count && _keys[r] < _keys[l]) ? r : l;
                    if (_keys[m] >= lastKey) break;
                    _items[i] = _items[m];
                    _keys[i] = _keys[m];
                    i = m;
                }
                if (Count > 0)
                {
                    _items[i] = lastItem;
                    _keys[i] = lastKey;
                }
                return top;
            }
        }
    }
}
