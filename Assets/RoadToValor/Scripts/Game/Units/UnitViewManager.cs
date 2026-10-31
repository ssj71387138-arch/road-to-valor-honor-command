using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>Creates, pools and updates UnitViews for every simulation unit (object pooling per model).</summary>
    public sealed class UnitViewManager
    {
        private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>(512);
        private readonly List<UnitView> _dying = new List<UnitView>(128);
        private readonly Dictionary<string, Stack<UnitView>> _pool = new Dictionary<string, Stack<UnitView>>();
        private readonly Transform _root;
        private readonly MapData _map;
        private readonly List<int> _removeIds = new List<int>(64);
        private readonly Plane[] _frustum = new Plane[6];
        public readonly List<UnitView> Active = new List<UnitView>(512);
        public int PooledCount { get; private set; }
        public System.Action<UnitView> OnCrashImpact;

        public UnitViewManager(Transform root, MapData map)
        {
            _root = root;
            _map = map;
        }

        public UnitView Get(int id) => _views.TryGetValue(id, out var v) ? v : null;

        private UnitView Create(SimUnit u)
        {
            string key = u.Def.Id + "_" + u.Team;
            UnitView view = null;
            if (_pool.TryGetValue(key, out var stack) && stack.Count > 0) { view = stack.Pop(); PooledCount--; }
            if (view == null)
            {
                var go = UnitModelFactory.Build(u.Def, u.Team);
                go.transform.SetParent(_root, false);
                view = go.AddComponent<UnitView>();
                view.Init(go.GetComponent<UnitRig>(), key, _map);
            }
            view.Bind(u);
            return view;
        }

        private void Release(UnitView v)
        {
            v.gameObject.SetActive(false);
            v.Unit = null;
            if (!_pool.TryGetValue(v.PoolKey, out var stack)) { stack = new Stack<UnitView>(); _pool[v.PoolKey] = stack; }
            stack.Push(v);
            PooledCount++;
        }

        /// <summary>Make sure every live simulation unit has a view; hand dead ones to the dying list.</summary>
        public void Sync(BattleSimulation sim)
        {
            foreach (var u in sim.UnitList)
            {
                if (!u.Alive) continue;
                if (!_views.ContainsKey(u.Id)) _views[u.Id] = Create(u);
            }
            _removeIds.Clear();
            foreach (var kv in _views)
                if (kv.Value.Unit == null || !kv.Value.Unit.Alive) _removeIds.Add(kv.Key);
            foreach (int id in _removeIds)
            {
                var v = _views[id];
                _views.Remove(id);
                if (v.Unit != null) _dying.Add(v);
                else Release(v);
            }
        }

        public void Update(float alpha, float dt, float simTime, Camera cam)
        {
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Plane[] frustum = null;
            if (cam != null)
            {
                GeometryUtility.CalculateFrustumPlanes(cam, _frustum);
                frustum = _frustum;
            }
            Active.Clear();
            foreach (var v in _views.Values)
            {
                v.Tick(alpha, dt, simTime, camPos, frustum);
                Active.Add(v);
            }
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var v = _dying[i];
                bool keep = v.Tick(1f, dt, simTime, camPos, frustum);
                if (v.CrashImpactPending) { v.CrashImpactPending = false; OnCrashImpact?.Invoke(v); }
                if (!keep) { _dying.RemoveAt(i); Release(v); }
            }
        }

        public void Clear()
        {
            foreach (var v in _views.Values) Release(v);
            foreach (var v in _dying) Release(v);
            _views.Clear();
            _dying.Clear();
            Active.Clear();
        }

        public int ViewCount => _views.Count + _dying.Count;
    }
}
