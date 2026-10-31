using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Visual for one simulation unit: interpolated transform, terrain following, animation state machine,
    /// selection ring, death/wreck sequence, LOD and off-screen animation skipping.
    /// </summary>
    public sealed class UnitView : MonoBehaviour
    {
        public const float InfantryScale = 1.5f;
        public const float VehicleScale = 1.15f;
        public const float AircraftScale = 1.2f;

        public SimUnit Unit;
        public UnitRig Rig;
        public string PoolKey;
        public bool Selected;
        public bool OnScreen;
        public Vector3 WorldPos;
        public float VisualScale = 1f;

        private IUnitAnimator _anim;
        private UnitAnimState _state = (UnitAnimState)(-1);
        private float _stateTime;
        private float _deathElapsed;
        private float _yaw;
        private Vector3 _lastPos;
        private Transform _ring;
        private MaterialPropertyBlock _mpb;
        private int _frame;
        private bool _wrecked;
        private bool _shadowsOn = true;
        private MapData _map;
        private bool _crashExploded;
        public bool CrashImpactPending;

        public bool IsDying => Unit != null && !Unit.Alive;
        public float Height => (Rig != null ? Rig.Height : 2f) * VisualScale;

        public void Init(UnitRig rig, string poolKey, MapData map)
        {
            Rig = rig;
            PoolKey = poolKey;
            _map = map;
            if (rig.Animator != null) _anim = new MecanimAnimatorDriver(rig);
            else
            {
                switch (rig.Kind)
                {
                    case RigKind.Soldier: _anim = new SoldierAnimator(rig); break;
                    case RigKind.Vehicle: _anim = new VehicleAnimator(rig); break;
                    case RigKind.Aircraft: _anim = new AircraftAnimator(rig); break;
                    default: _anim = new StructureAnimator(rig); break;
                }
            }
        }

        public void Bind(SimUnit u)
        {
            Unit = u;
            _state = (UnitAnimState)(-1);
            _stateTime = 0f;
            _deathElapsed = 0f;
            _wrecked = false;
            _crashExploded = false;
            CrashImpactPending = false;
            Selected = false;
            _yaw = u.Yaw * Mathf.Rad2Deg;
            var d = u.Def;
            VisualScale = d.IsInfantry ? InfantryScale : (d.IsVehicle ? VehicleScale : (d.IsAir ? AircraftScale : 1f));
            transform.localScale = Vector3.one * VisualScale;
            if (_mpb != null) foreach (var r in Rig.Renderers) r.SetPropertyBlock(null);
            if (_ring != null) _ring.gameObject.SetActive(false);
            Place(u.Pos, 0f, true);
            _lastPos = transform.position;
            gameObject.SetActive(true);
        }

        private float AirAltitude(UnitDefinition d)
        {
            switch (d.Visual)
            {
                case "air_drone": case "air_flaredrone": return 22f;
                case "air_kamikaze": return 14f;
                case "air_attackheli": return 26f;
                case "air_cargoheli": return 30f;
                case "air_osprey": return 34f;
                default: return 48f;
            }
        }

        private void Place(Vec2 p, float simTime, bool snap)
        {
            float ground = _map.GroundHeight(p);
            float y = ground;
            var u = Unit;
            if (u.IsAir)
            {
                float alt = AirAltitude(u.Def);
                float climb = Mathf.Clamp01((simTime - u.SpawnTime) / 3f);
                if (u.Exiting) alt += 30f * (1f - Mathf.Clamp01(u.Lifetime / 10f));
                if (u.Def.Ability == AbilityType.Suicide && u.TargetId != 0) alt = Mathf.Lerp(2f, alt, Mathf.Clamp01(Vec2.Distance(u.Pos, u.AimPoint) / 40f));
                y = Mathf.Max(ground, _map.WaterLevel) + alt * Mathf.Lerp(0.3f, 1f, climb);
            }
            else if (u.DropTimer > 0f)
            {
                float dropTotal = Mathf.Max(0.5f, u.Def.AbP1);
                y = ground + 70f * Mathf.Clamp01(u.DropTimer / dropTotal);
            }
            transform.position = new Vector3(p.X, y, p.Z);
        }

        public void SetSelected(bool on, Team team)
        {
            Selected = on;
            if (on && _ring == null)
            {
                var go = new GameObject("SelectionRing");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("sel_ring", b => b.Ring(Vector3.zero, 0.86f, 1f, Palette.White, 40));
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = SelectionMaterial(team);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ring = go.transform;
            }
            if (_ring != null)
            {
                _ring.gameObject.SetActive(on);
                if (on)
                {
                    float r = Mathf.Max(0.9f, Unit.Radius * 1.5f + 0.5f) / VisualScale;
                    _ring.localScale = new Vector3(r, 1f, r);
                    _ring.localPosition = new Vector3(0f, 0.15f / VisualScale, 0f);
                }
            }
        }

        private static Material _selBlue, _selRed;
        private static Material SelectionMaterial(Team t)
        {
            if (t == Team.Enemy) { if (_selRed == null) _selRed = MaterialLibrary.CreateUnlit(new Color(1f, 0.25f, 0.2f, 0.95f)); return _selRed; }
            if (_selBlue == null) _selBlue = MaterialLibrary.CreateUnlit(new Color(0.25f, 0.65f, 1f, 0.95f));
            return _selBlue;
        }

        /// <summary>Returns false once a dead unit's death sequence is complete (ready for the pool).</summary>
        public bool Tick(float alpha, float dt, float simTime, Vector3 camPos, Plane[] frustum)
        {
            var u = Unit;
            bool alive = u.Alive;
            Vec2 p = alive ? Vec2.Lerp(u.PrevPos, u.Pos, alpha) : u.Pos;

            if (alive || u.IsAir)
            {
                if (alive) Place(p, simTime, false);
            }

            // Aircraft crash: fall to the ground.
            if (!alive && u.IsAir && !_crashExploded)
            {
                var pos = transform.position;
                float ground = Mathf.Max(_map.GroundHeight(u.Pos), _map.WaterLevel);
                pos.y = Mathf.MoveTowards(pos.y, ground, (12f + _deathElapsed * 30f) * dt);
                pos += transform.forward * dt * 10f;
                transform.position = pos;
                if (pos.y <= ground + 0.3f) { _crashExploded = true; CrashImpactPending = true; }
            }

            // Facing.
            float targetYaw = u.Yaw * Mathf.Rad2Deg;
            _yaw = Mathf.LerpAngle(_yaw, targetYaw, 1f - Mathf.Exp(-dt * (u.IsInfantry ? 14f : 6f)));
            Quaternion rot = Quaternion.Euler(0f, _yaw, 0f);
            if (u.IsVehicle && alive)
            {
                // Align to terrain slope.
                Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;
                float L = u.Radius * 1.2f;
                Vec2 f = p + new Vec2(fwd.x, fwd.z) * L, bk = p - new Vec2(fwd.x, fwd.z) * L;
                Vec2 rt = p + new Vec2(right.x, right.z) * L * 0.6f, lf = p - new Vec2(right.x, right.z) * L * 0.6f;
                float pitch = Mathf.Atan2(_map.GroundHeight(bk) - _map.GroundHeight(f), 2f * L) * Mathf.Rad2Deg;
                float roll = Mathf.Atan2(_map.GroundHeight(lf) - _map.GroundHeight(rt), 1.2f * L) * Mathf.Rad2Deg;
                rot = rot * Quaternion.Euler(Mathf.Clamp(pitch, -25f, 25f), 0f, Mathf.Clamp(-roll, -20f, 20f));
            }
            transform.rotation = rot;

            // Movement metrics.
            Vector3 now = transform.position;
            Vector3 delta = now - _lastPos;
            delta.y = 0f;
            float moved = delta.magnitude;
            _lastPos = now;
            float speed = dt > 0f ? moved / dt : 0f;
            float signedDist = Vector3.Dot(delta, transform.forward);
            WorldPos = now;

            // Visibility / LOD.
            float camDist = Vector3.Distance(camPos, now);
            var bounds = new Bounds(now + Vector3.up * Height * 0.5f, new Vector3(Height + 2f, Height + 2f, Height + 2f));
            OnScreen = frustum == null || GeometryUtility.TestPlanesAABB(frustum, bounds);
            bool far = camDist > 260f;
            bool wantShadows = !far || !u.IsInfantry;
            if (wantShadows != _shadowsOn)
            {
                _shadowsOn = wantShadows;
                foreach (var r in Rig.Renderers) if (r != null) r.shadowCastingMode = wantShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // Animation state machine.
            var st = UnitAnimStateMachine.Resolve(u, simTime, alive ? u.Velocity.Length : 0f);
            if (st != _state)
            {
                _state = st;
                _stateTime = 0f;
                _anim.OnStateEnter(st);
            }
            _stateTime += dt;
            if (!alive) _deathElapsed += dt;

            _frame++;
            bool animate = OnScreen && (!far || (_frame % 3) == 0 || !alive);
            if (animate)
            {
                var ctx = new AnimContext
                {
                    Speed = speed,
                    MaxSpeed = u.Def.MoveSpeed,
                    TurretYawLocal = Mathf.DeltaAngle(u.Yaw * Mathf.Rad2Deg, u.TurretYaw * Mathf.Rad2Deg),
                    StateTime = _stateTime,
                    Dt = far ? dt * 3f : dt,
                    Time = simTime,
                    HpRatio = u.HpRatio,
                    BuildProgress = u.BuildProgress,
                    DeathTime = _deathElapsed,
                    Distance = signedDist,
                    Ability = u.Def.Ability,
                };
                _anim.Animate(st, ref ctx);
            }

            // Wreck darkening for destroyed vehicles / structures.
            if (!alive && !_wrecked && !u.IsInfantry && _deathElapsed > 0.15f)
            {
                _wrecked = true;
                if (_mpb == null) _mpb = new MaterialPropertyBlock();
                _mpb.SetColor("_BaseColor", new Color(0.28f, 0.26f, 0.25f));
                _mpb.SetColor("_Color", new Color(0.28f, 0.26f, 0.25f));
                foreach (var r in Rig.Renderers) if (r != null) r.SetPropertyBlock(_mpb);
            }
            if (_ring != null && _ring.gameObject.activeSelf && !alive) _ring.gameObject.SetActive(false);

            if (!alive)
            {
                float hold = u.IsInfantry ? 6f : (u.IsStructure ? 3f : (u.IsAir ? 8f : 14f));
                if (_deathElapsed > hold) return false;
            }
            return true;
        }
    }
}
