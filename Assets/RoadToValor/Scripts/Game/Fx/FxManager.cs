using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    /// <summary>
    /// Pooled battle effects with hard caps (tracers, shells/rockets, explosions, smoke, muzzle flashes,
    /// heal/repair beams, tesla arcs, mines, missile warnings, airstrike flybys, command markers, falling snow).
    /// Particle systems are shared per effect type and fed with Emit() so thousands of effects cost a few draw calls.
    /// </summary>
    public sealed class FxManager
    {
        private readonly Transform _root;
        private readonly MapData _map;
        private ParticleSystem _fire, _smoke, _dirt, _flash, _sparks, _snow, _waterSplash;
        private readonly List<Line> _lines = new List<Line>();
        private readonly List<Shell> _shells = new List<Shell>();
        private readonly List<Marker> _markers = new List<Marker>();
        private readonly List<Flyby> _flybys = new List<Flyby>();
        private readonly Stack<LineRenderer> _linePool = new Stack<LineRenderer>();
        private readonly Stack<Transform> _shellPool = new Stack<Transform>();
        private readonly Dictionary<int, Transform> _mines = new Dictionary<int, Transform>();
        private readonly Stack<Transform> _minePool = new Stack<Transform>();
        private Material _lineMat;
        private Mesh _shellMesh, _rocketMesh, _mineMesh, _markerMesh, _warnMesh;
        private Material _markerMove, _markerAttack, _warnMat, _mineOwnMat, _mineEnemyMat, _crateMat;
        public int MaxLines = 160;
        public int MaxShells = 220;
        public bool SnowEnabled = true;

        private sealed class Line { public LineRenderer R; public float Life, Max; public Color C; }
        private sealed class Shell { public Transform T; public Vector3 From, To; public float Elapsed, Duration, Arc; public bool Rocket; }
        private sealed class Marker { public Transform T; public float Life, Max; public Material M; public bool Warn; public float Radius; }
        private sealed class Flyby { public Transform T; public Vector3 From, To; public float Elapsed, Duration; public Vector3 DropAt; public bool Dropped; public int Kind; public Transform Crate; }

        public FxManager(Transform parent, MapData map)
        {
            _root = new GameObject("FX").transform;
            _root.SetParent(parent, false);
            _map = map;
            _lineMat = MaterialLibrary.CreateUnlit(Color.white);
            _fire = MakeSystem("Fire", 900, 1f);
            _smoke = MakeSystem("Smoke", 1100, 0.6f);
            _dirt = MakeSystem("Dirt", 700, 1f, 1f);
            _flash = MakeSystem("Flash", 400, 1f);
            _sparks = MakeSystem("Sparks", 500, 1f, 0.4f);
            _waterSplash = MakeSystem("Splash", 300, 1f, 0.8f);
            _snow = MakeSnow();
            _shellMesh = UnitModelFactory.GetMesh("fx_shell", b => b.Sphere(Vector3.zero, new Vector3(0.35f, 0.35f, 0.6f), Palette.Gunmetal, 4, 6));
            _rocketMesh = UnitModelFactory.GetMesh("fx_rocket", b => { b.Tube(Vector3.zero, 0.12f, 1.2f, Palette.AirGrey, 6); b.Tube(new Vector3(0, 0, 0.7f), 0.12f, 0.3f, Palette.MedicRed, 6, default(Vector3), 0.01f); });
            _mineMesh = UnitModelFactory.GetMesh("fx_mine", b => { b.Cylinder(Vector3.zero, 0.5f, 0.15f, Palette.TankOlive, 10); b.Cylinder(new Vector3(0, 0.1f, 0), 0.12f, 0.08f, Palette.Gunmetal, 6); });
            _markerMesh = UnitModelFactory.GetMesh("fx_marker", b => b.Ring(Vector3.zero, 0.8f, 1f, Palette.White, 32));
            _warnMesh = UnitModelFactory.GetMesh("fx_warn", b => { b.Ring(Vector3.zero, 0.94f, 1f, Palette.White, 48); b.Ring(Vector3.zero, 0.05f, 0.12f, Palette.White, 16); });
            _markerMove = MaterialLibrary.CreateUnlit(new Color(0.3f, 1f, 0.45f, 0.9f));
            _markerAttack = MaterialLibrary.CreateUnlit(new Color(1f, 0.25f, 0.2f, 0.9f));
            _warnMat = MaterialLibrary.CreateUnlit(new Color(1f, 0.1f, 0.05f, 0.8f));
            _mineOwnMat = MaterialLibrary.PaletteMaterial;
            _mineEnemyMat = MaterialLibrary.CreateUnlit(new Color(1f, 0.3f, 0.2f, 1f));
            _crateMat = MaterialLibrary.PaletteMaterial;
        }

        // ------------------------------------------------------------------ particle systems

        private ParticleSystem MakeSystem(string name, int max, float alphaFade, float gravity = 0f)
        {
            var go = new GameObject("PS_" + name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 1f;
            main.startSpeed = 0f;
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(alphaFade, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, name == "Smoke" ? 2.2f : 1.2f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.ParticleMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private ParticleSystem MakeSnow()
        {
            var go = new GameObject("PS_SnowWeather");
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.maxParticles = 2500;
            main.startLifetime = 9f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.45f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.025f;
            var em = ps.emission;
            em.rateOverTime = 260f;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(260f, 1f, 260f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            vel.y = new ParticleSystem.MinMaxCurve(-3.5f, -2.2f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = MaterialLibrary.ParticleMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        private static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float size, float life, Color c)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = vel,
                startSize = size,
                startLifetime = life,
                startColor = c,
                applyShapeToPosition = false,
            };
            ps.Emit(ep, 1);
        }

        public void FollowCamera(Vector3 focus)
        {
            if (_snow == null) return;
            _snow.transform.position = focus + Vector3.up * 70f;
            var em = _snow.emission;
            em.enabled = SnowEnabled;
        }

        // ------------------------------------------------------------------ effect API

        private float H(Vec2 p) => Mathf.Max(_map.GroundHeight(p), _map.WaterLevel);

        public void MuzzleFlash(Vector3 p, DamageType type)
        {
            Color c = type == DamageType.Flame ? new Color(1f, 0.55f, 0.15f, 1f) : new Color(1f, 0.85f, 0.45f, 1f);
            float size = type == DamageType.AP || type == DamageType.HE ? 1.8f : 0.7f;
            Emit(_flash, p, Vector3.zero, size, 0.07f, c);
            if (type == DamageType.AP || type == DamageType.HE) Emit(_smoke, p, Vector3.up * 1.5f, 2.2f, 1.6f, new Color(0.7f, 0.7f, 0.68f, 0.5f));
        }

        public void Tracer(Vector3 from, Vector3 to, DamageType type)
        {
            if (_lines.Count >= MaxLines) return;
            Color c;
            float width = 0.08f, life = 0.07f;
            switch (type)
            {
                case DamageType.Flame:
                    // flamethrower stream: particles instead of a line
                    for (int i = 0; i < 6; i++)
                    {
                        Vector3 p = Vector3.Lerp(from, to, i / 6f) + Random.insideUnitSphere * 0.4f;
                        Emit(_fire, p, (to - from).normalized * 6f + Vector3.up, Random.Range(0.8f, 1.8f), 0.35f, new Color(1f, Random.Range(0.4f, 0.7f), 0.1f, 1f));
                    }
                    return;
                case DamageType.Sniper: c = new Color(1f, 1f, 0.85f, 1f); width = 0.06f; life = 0.12f; break;
                case DamageType.AA: c = new Color(1f, 0.6f, 0.2f, 1f); width = 0.12f; break;
                default: c = new Color(1f, 0.8f, 0.35f, 1f); break;
            }
            AddLine(new[] { from, to }, c, width, life);
            Emit(_sparks, to, Random.insideUnitSphere * 2f + Vector3.up, 0.25f, 0.2f, new Color(1f, 0.8f, 0.4f, 1f));
            if (to.y <= _map.WaterLevel + 0.5f) Emit(_waterSplash, to, Vector3.up * 3f, 0.6f, 0.4f, new Color(0.8f, 0.85f, 0.9f, 0.8f));
            else if (Random.value < 0.3f) Emit(_dirt, to, Vector3.up * 2f + Random.insideUnitSphere, 0.4f, 0.5f, new Color(0.35f, 0.3f, 0.25f, 1f));
        }

        public void Beam(Vector3 from, Vector3 to, Color c, float life = 0.25f, float width = 0.12f)
        {
            if (_lines.Count >= MaxLines) return;
            AddLine(new[] { from, to }, c, width, life);
        }

        public void TeslaArc(Vector3 from, Vector3 to)
        {
            if (_lines.Count >= MaxLines) return;
            var pts = new Vector3[9];
            for (int i = 0; i < pts.Length; i++)
            {
                float t = i / (float)(pts.Length - 1);
                pts[i] = Vector3.Lerp(from, to, t) + (i > 0 && i < pts.Length - 1 ? Random.insideUnitSphere * 1.2f : Vector3.zero);
            }
            AddLine(pts, new Color(0.55f, 0.85f, 1f, 1f), 0.22f, 0.18f);
            for (int i = 0; i < 6; i++) Emit(_sparks, to, Random.insideUnitSphere * 5f, 0.35f, 0.3f, new Color(0.6f, 0.9f, 1f, 1f));
        }

        private void AddLine(Vector3[] pts, Color c, float width, float life)
        {
            LineRenderer lr;
            if (_linePool.Count > 0) lr = _linePool.Pop();
            else
            {
                var go = new GameObject("Line");
                go.transform.SetParent(_root, false);
                lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = _lineMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.useWorldSpace = true;
                lr.numCapVertices = 0;
            }
            lr.gameObject.SetActive(true);
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            lr.startColor = c;
            lr.endColor = c;
            _lines.Add(new Line { R = lr, Life = life, Max = life, C = c });
        }

        public void LaunchShell(Vector3 from, Vector3 to, float duration, bool indirect, bool rocket)
        {
            if (_shells.Count >= MaxShells) return;
            Transform t;
            if (_shellPool.Count > 0) t = _shellPool.Pop();
            else
            {
                var go = new GameObject("Shell");
                go.transform.SetParent(_root, false);
                go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialLibrary.PaletteMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                t = go.transform;
            }
            t.GetComponent<MeshFilter>().sharedMesh = rocket ? _rocketMesh : _shellMesh;
            t.gameObject.SetActive(true);
            t.position = from;
            float dist = Vector3.Distance(from, to);
            _shells.Add(new Shell { T = t, From = from, To = to, Duration = Mathf.Max(0.05f, duration), Arc = indirect ? Mathf.Max(8f, dist * 0.35f) : dist * 0.02f, Rocket = rocket });
        }

        public void Explosion(Vector3 p, float radius, bool big)
        {
            int fire = Mathf.Clamp((int)(radius * (big ? 6f : 3f)), 4, 60);
            for (int i = 0; i < fire; i++)
                Emit(_fire, p + Random.insideUnitSphere * radius * 0.3f, Random.insideUnitSphere * radius * 1.4f + Vector3.up * radius * 0.8f,
                    Random.Range(radius * 0.4f, radius * 0.9f) + 0.6f, Random.Range(0.35f, 0.8f), new Color(1f, Random.Range(0.35f, 0.75f), 0.12f, 1f));
            int smoke = Mathf.Clamp((int)(radius * (big ? 4f : 2f)), 3, 40);
            for (int i = 0; i < smoke; i++)
                Emit(_smoke, p + Random.insideUnitSphere * radius * 0.5f + Vector3.up, Random.insideUnitSphere * 1.5f + Vector3.up * Random.Range(1.5f, 4f),
                    Random.Range(radius * 0.6f, radius * 1.2f) + 1.5f, Random.Range(2.5f, 5f), new Color(0.24f, 0.23f, 0.22f, 0.7f));
            int dirt = Mathf.Clamp((int)(radius * 3f), 4, 40);
            bool water = p.y <= _map.WaterLevel + 0.6f;
            for (int i = 0; i < dirt; i++)
            {
                Vector3 v = new Vector3(Random.Range(-1f, 1f), Random.Range(1.2f, 2.4f), Random.Range(-1f, 1f)) * radius * 1.5f;
                if (water) Emit(_waterSplash, p, v, Random.Range(0.4f, 1.2f), Random.Range(0.8f, 1.4f), new Color(0.75f, 0.82f, 0.88f, 0.9f));
                else Emit(_dirt, p, v, Random.Range(0.25f, 0.7f), Random.Range(0.8f, 1.6f), new Color(0.27f, 0.22f, 0.17f, 1f));
            }
            Emit(_flash, p + Vector3.up, Vector3.zero, radius * 3f, 0.12f, new Color(1f, 0.9f, 0.6f, 1f));
        }

        public void Smoke(Vector3 p, float size, Color c)
        {
            Emit(_smoke, p + Random.insideUnitSphere * 0.5f, Vector3.up * Random.Range(1.5f, 3f) + Random.insideUnitSphere * 0.4f, size, Random.Range(2f, 4f), c);
        }

        public void Burn(Vector3 p, float size)
        {
            Emit(_fire, p + Random.insideUnitSphere * size * 0.3f, Vector3.up * Random.Range(1f, 2.5f), size * Random.Range(0.6f, 1f), Random.Range(0.4f, 0.8f), new Color(1f, Random.Range(0.35f, 0.65f), 0.1f, 1f));
        }

        public void Dust(Vector3 p, float size)
        {
            Emit(_smoke, p, Vector3.up * 0.8f + Random.insideUnitSphere * 0.8f, size, Random.Range(1f, 2f), new Color(0.6f, 0.58f, 0.55f, 0.35f));
        }

        public void HealSparkle(Vector3 p, Color c)
        {
            for (int i = 0; i < 3; i++) Emit(_sparks, p + Random.insideUnitSphere * 0.6f, Vector3.up * 1.5f, 0.3f, 0.6f, c);
        }

        public void CommandMarker(Vector3 p, bool attack)
        {
            AddMarker(p, attack ? _markerAttack : _markerMove, 0.6f, 2.2f, false);
        }

        public void MissileWarning(Vector3 p, float radius, float duration)
        {
            AddMarker(p, _warnMat, duration, radius, true);
        }

        private void AddMarker(Vector3 p, Material m, float life, float radius, bool warn)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(_root, false);
            go.transform.position = p + Vector3.up * 0.4f;
            go.AddComponent<MeshFilter>().sharedMesh = warn ? _warnMesh : _markerMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(radius, 1f, radius);
            _markers.Add(new Marker { T = go.transform, Life = life, Max = life, M = m, Warn = warn, Radius = radius });
        }

        public void Flyover(UnitDefinition def, Team team, Vec2 target, Vec2 dir, float delay, int kind)
        {
            string visual = def.Id == 52 ? "air_bomber" : (def.Id == 54 ? "air_supplydrop" : (def.Id == 55 ? "air_cargoplane" : "air_fighter"));
            var fake = new UnitDefinition { Id = 9000 + def.Id, Key = "Flyby_" + def.Key, Visual = visual, Mobility = Mobility.Air };
            var go = UnitModelFactory.Build(fake, team);
            go.transform.SetParent(_root, false);
            Vector3 d = new Vector3(dir.X, 0f, dir.Z);
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            d.Normalize();
            float alt = 70f;
            Vector3 tgt = new Vector3(target.X, H(target), target.Z);
            Vector3 from = tgt - d * 450f + Vector3.up * alt;
            Vector3 to = tgt + d * 450f + Vector3.up * alt;
            float speed = 900f / Mathf.Max(0.5f, delay * 2f);
            go.transform.position = from;
            go.transform.rotation = Quaternion.LookRotation(d);
            go.transform.localScale = Vector3.one * 1.3f;
            _flybys.Add(new Flyby { T = go.transform, From = from, To = to, Duration = 900f / speed, DropAt = tgt, Kind = kind });
        }

        // ------------------------------------------------------------------ mines

        public void SyncMines(BattleSimulation sim, Team viewer)
        {
            foreach (var m in sim.Mines)
            {
                bool visible = m.Alive && (m.Team == viewer || m.Revealed);
                _mines.TryGetValue(m.Id, out var t);
                if (visible && t == null)
                {
                    if (_minePool.Count > 0) t = _minePool.Pop();
                    else
                    {
                        var go = new GameObject("Mine");
                        go.transform.SetParent(_root, false);
                        go.AddComponent<MeshFilter>().sharedMesh = _mineMesh;
                        go.AddComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        t = go.transform;
                    }
                    t.gameObject.SetActive(true);
                    t.GetComponent<MeshRenderer>().sharedMaterial = m.Team == viewer ? _mineOwnMat : _mineEnemyMat;
                    t.position = new Vector3(m.Pos.X, _map.GroundHeight(m.Pos) + 0.05f, m.Pos.Z);
                    _mines[m.Id] = t;
                }
                else if (!visible && t != null)
                {
                    t.gameObject.SetActive(false);
                    _minePool.Push(t);
                    _mines.Remove(m.Id);
                }
            }
            if (_mines.Count > 0)
            {
                var dead = new List<int>();
                foreach (var kv in _mines)
                {
                    bool found = false;
                    foreach (var m in sim.Mines) if (m.Id == kv.Key && m.Alive) { found = true; break; }
                    if (!found) dead.Add(kv.Key);
                }
                foreach (int id in dead) { _mines[id].gameObject.SetActive(false); _minePool.Push(_mines[id]); _mines.Remove(id); }
            }
        }

        // ------------------------------------------------------------------ update

        public void Update(float dt)
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                l.Life -= dt;
                if (l.Life <= 0f) { l.R.gameObject.SetActive(false); _linePool.Push(l.R); _lines.RemoveAt(i); continue; }
                Color c = l.C;
                c.a = l.Life / l.Max;
                l.R.startColor = c;
                l.R.endColor = c;
            }
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                var s = _shells[i];
                s.Elapsed += dt;
                float t = Mathf.Clamp01(s.Elapsed / s.Duration);
                Vector3 p = Vector3.Lerp(s.From, s.To, t) + Vector3.up * (s.Arc * 4f * t * (1f - t));
                Vector3 prev = s.T.position;
                s.T.position = p;
                if ((p - prev).sqrMagnitude > 1e-4f) s.T.rotation = Quaternion.LookRotation(p - prev);
                if (s.Rocket && Random.value < 0.7f) Emit(_smoke, prev, Vector3.up * 0.3f, 0.9f, 1.2f, new Color(0.75f, 0.75f, 0.72f, 0.5f));
                if (t >= 1f) { s.T.gameObject.SetActive(false); _shellPool.Push(s.T); _shells.RemoveAt(i); }
            }
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                m.Life -= dt;
                if (m.Life <= 0f) { Object.Destroy(m.T.gameObject); _markers.RemoveAt(i); continue; }
                float k = m.Life / m.Max;
                if (m.Warn) m.T.Rotate(0f, 90f * dt, 0f);
                else m.T.localScale = Vector3.one * Mathf.Lerp(0.3f, m.Radius, k);
            }
            for (int i = _flybys.Count - 1; i >= 0; i--)
            {
                var f = _flybys[i];
                f.Elapsed += dt;
                float t = f.Elapsed / f.Duration;
                f.T.position = Vector3.Lerp(f.From, f.To, t);
                if (!f.Dropped && t >= 0.5f)
                {
                    f.Dropped = true;
                    if (f.Kind == 2) // supply crate under parachute
                    {
                        var crate = new GameObject("SupplyCrate");
                        crate.transform.SetParent(_root, false);
                        crate.AddComponent<MeshFilter>().sharedMesh = UnitModelFactory.GetMesh("supply_crate", b =>
                        {
                            b.Box(Vector3.zero, new Vector3(1.6f, 1.2f, 1.6f), Palette.Crate);
                            b.Box(new Vector3(0, 0.61f, 0), new Vector3(1.2f, 0.02f, 0.2f), Palette.White);
                            b.Sphere(new Vector3(0, 6f, 0), new Vector3(5f, 2.4f, 5f), Palette.Canvas, 6, 10, true);
                        });
                        crate.AddComponent<MeshRenderer>().sharedMaterial = _crateMat;
                        crate.transform.position = f.T.position - Vector3.up * 5f;
                        f.Crate = crate.transform;
                    }
                }
                if (f.Crate != null)
                {
                    var cp = f.Crate.position;
                    cp.y = Mathf.MoveTowards(cp.y, f.DropAt.y + 0.6f, 14f * dt);
                    f.Crate.position = new Vector3(Mathf.MoveTowards(cp.x, f.DropAt.x, 20f * dt), cp.y, Mathf.MoveTowards(cp.z, f.DropAt.z, 20f * dt));
                }
                if (t >= 1f)
                {
                    Object.Destroy(f.T.gameObject);
                    if (f.Crate != null) Object.Destroy(f.Crate.gameObject, 8f);
                    _flybys.RemoveAt(i);
                }
            }
        }

        public void Clear()
        {
            foreach (var l in _lines) { l.R.gameObject.SetActive(false); _linePool.Push(l.R); }
            _lines.Clear();
            foreach (var s in _shells) { s.T.gameObject.SetActive(false); _shellPool.Push(s.T); }
            _shells.Clear();
            foreach (var m in _markers) Object.Destroy(m.T.gameObject);
            _markers.Clear();
            foreach (var f in _flybys) { Object.Destroy(f.T.gameObject); if (f.Crate != null) Object.Destroy(f.Crate.gameObject); }
            _flybys.Clear();
            foreach (var kv in _mines) { kv.Value.gameObject.SetActive(false); _minePool.Push(kv.Value); }
            _mines.Clear();
            foreach (var ps in new[] { _fire, _smoke, _dirt, _flash, _sparks, _waterSplash }) ps.Clear();
        }
    }
}
