using System.Collections.Generic;
using RoadToValor.Core;
using UnityEngine;

namespace RoadToValor.Game
{
    public sealed class Notice
    {
        public string Text;
        public Color Color;
        public float Time;
        public Vec2 Pos;
        public bool HasPos;
    }

    public sealed class MinimapPing
    {
        public Vec2 Pos;
        public Color Color;
        public float Time;
    }

    /// <summary>
    /// Runs one battle: steps the fixed-tick simulation, interpolates views, turns simulation events into
    /// effects / notifications and feeds the HUD. Also runs the silent AI-vs-AI demo battle behind the lobby.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        public GameRoot Root;
        public BattleSimulation Sim;
        public CommandController Cmd;
        public UnitViewManager Views;
        public FxManager Fx;
        public bool IsDemo;
        public bool Paused;
        public float Alpha;
        public float SimSpeed = 1f;
        public readonly List<Notice> Notices = new List<Notice>();
        public readonly List<MinimapPing> Pings = new List<MinimapPing>();
        public MatchRecord Result;
        public bool ResultRecorded;
        public Team PlayerTeam = Team.Player;

        private float _acc;
        private float _ambientTimer;
        private float _lastHqWarn = -99f;
        private float _lastHqHp;
        private int _shotsThisFrame;
        private Transform _unitRoot;

        public void Begin(GameData data, BattleSetup setup, bool demo)
        {
            End();
            IsDemo = demo;
            Paused = false;
            Result = null;
            ResultRecorded = false;
            Notices.Clear();
            Pings.Clear();
            Sim = new BattleSimulation(data, setup);
            if (_unitRoot == null) { _unitRoot = new GameObject("Units").transform; _unitRoot.SetParent(transform, false); }
            if (Views == null) Views = new UnitViewManager(_unitRoot, data.Map);
            if (Fx == null) Fx = new FxManager(transform, data.Map);
            Views.OnCrashImpact = v => Fx.Explosion(v.transform.position, 6f, true);
            Fx.SnowEnabled = Root.Profile.snow;
            Cmd = new CommandController(Sim, Root.CamRig, Views, Fx, PlayerTeam);
            Cmd.IsOverUi = p => Root.Hud != null && Root.Hud.IsOverUi(p);
            _acc = 0f;
            var hq = Sim.Hq(PlayerTeam);
            _lastHqHp = hq != null ? hq.Hp : 0f;
            if (!demo)
            {
                Notify("작전 개시! 3개 공격로로 적 본진을 향해 진격하라.", UiTheme.Gold);
                Notify("WASD 카메라 · 좌클릭 선택 · 우클릭 이동/공격 · F1~F8 카드 배치", UiTheme.TextDim);
            }
        }

        public void End()
        {
            if (Views != null) Views.Clear();
            if (Fx != null) Fx.Clear();
            if (Cmd != null) Cmd.ClearSelection();
            Sim = null;
            Cmd = null;
        }

        public void Notify(string text, Color c, Vec2? pos = null)
        {
            Notices.Add(new Notice { Text = text, Color = c, Time = Time.unscaledTime, Pos = pos ?? Vec2.Zero, HasPos = pos.HasValue });
            if (Notices.Count > 8) Notices.RemoveAt(0);
        }

        private void Ping(Vec2 p, Color c) => Pings.Add(new MinimapPing { Pos = p, Color = c, Time = Time.unscaledTime });

        private void Update()
        {
            if (Sim == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _shotsThisFrame = 0;

            if (!Paused)
            {
                _acc += dt * SimSpeed;
                int steps = 0;
                while (_acc >= BattleSimulation.Dt && steps < 6)
                {
                    Sim.Step();
                    ProcessEvents();
                    _acc -= BattleSimulation.Dt;
                    steps++;
                }
                if (steps == 6) _acc = 0f;
            }
            Alpha = Mathf.Clamp01(_acc / BattleSimulation.Dt);

            var cam = Root.MainCam;
            Views.Sync(Sim);
            Views.Update(Alpha, Paused ? 0f : dt, Sim.Time, cam);
            Fx.Update(Paused ? 0f : dt);
            Fx.SyncMines(Sim, PlayerTeam);
            Fx.FollowCamera(Root.CamRig.Focus);

            bool placingStructure = false;
            int placingDef = -1;
            if (!IsDemo && Cmd != null && Cmd.PlacingDefId >= 0)
            {
                var d = Sim.Units.Get(Cmd.PlacingDefId);
                placingStructure = d != null && d.IsStructure;
                placingDef = Cmd.PlacingDefId;
            }
            Root.World.UpdateVisuals(Sim, Time.time, placingStructure, PlayerTeam, placingDef);

            if (!IsDemo && !Paused && !Sim.IsOver && Cmd != null) Cmd.Update(dt);
            if (!IsDemo && Sim.IsOver && !ResultRecorded) RecordResult();

            if (!Paused) Ambient(dt);
            for (int i = Pings.Count - 1; i >= 0; i--) if (Time.unscaledTime - Pings[i].Time > 3f) Pings.RemoveAt(i);
        }

        private void Ambient(float dt)
        {
            _ambientTimer -= dt;
            if (_ambientTimer > 0f) return;
            _ambientTimer = 0.18f;
            foreach (var v in Views.Active)
            {
                if (!v.OnScreen || v.Unit == null) continue;
                var u = v.Unit;
                if (u.IsInfantry) continue;
                Vector3 top = v.WorldPos + Vector3.up * v.Height * 0.8f;
                if (u.HpRatio < 0.5f && !u.Constructing) Fx.Smoke(top, u.IsStructure ? 4f : 2.5f, new Color(0.15f, 0.15f, 0.15f, 0.6f));
                if (u.HpRatio < 0.25f && !u.Constructing) Fx.Burn(top, u.IsStructure ? 2.5f : 1.4f);
                if (u.IsVehicle && u.Velocity.SqrLength > 4f) Fx.Dust(v.WorldPos - v.transform.forward * u.Radius, 2.2f);
                if (u.Constructing) Fx.Dust(v.WorldPos + Random.insideUnitSphere * 3f, 3f);
            }
        }

        private Vector3 MuzzleOf(int unitId, Vec2 fallback)
        {
            var v = Views.Get(unitId);
            if (v != null && v.Rig != null && v.Rig.Muzzle != null) return v.Rig.Muzzle.position;
            return fallback.ToV3(Root.Data.Map.GroundHeight(fallback) + 1.4f);
        }

        private Vector3 CenterOf(int unitId, Vec2 fallback)
        {
            var v = Views.Get(unitId);
            if (v != null) return v.WorldPos + Vector3.up * v.Height * 0.5f;
            return fallback.ToV3(Root.Data.Map.GroundHeight(fallback) + 1f);
        }

        private bool NearCamera(Vec2 p, float maxDist)
        {
            var f = Root.CamRig.Focus;
            return (new Vector2(p.X - f.x, p.Z - f.z)).sqrMagnitude < maxDist * maxDist;
        }

        private void ProcessEvents()
        {
            var map = Root.Data.Map;
            bool player = !IsDemo;
            foreach (var e in Sim.Events)
            {
                switch (e.Type)
                {
                    case SimEventType.Shot:
                    {
                        if (!NearCamera(e.Pos, 520f)) break;
                        if (++_shotsThisFrame > 90) break;
                        Vector3 from = MuzzleOf(e.UnitId, e.Pos);
                        Vector3 to = CenterOf(e.OtherId, e.Pos2) + Random.insideUnitSphere * 0.4f;
                        Fx.MuzzleFlash(from, e.Damage);
                        Fx.Tracer(from, to, e.Damage);
                        break;
                    }
                    case SimEventType.ProjectileLaunched:
                    {
                        if (!NearCamera(e.Pos, 700f)) break;
                        Vector3 from = MuzzleOf(e.UnitId, e.Pos);
                        var targetUnit = Sim.Get(e.OtherId);
                        float toY = map.GroundHeight(e.Pos2) + 0.5f;
                        Vector3 to = e.Pos2.ToV3(toY);
                        bool rocket = e.DefId == 25 || e.DefId == 70 || e.DefId == 47 || e.DefId == 5 || e.DefId == 59 || e.DefId == 53;
                        if (e.DefId == 59 || e.DefId == 53 || e.DefId == 22) to.y += 30f; // AA against aircraft
                        Fx.MuzzleFlash(from, e.Damage);
                        Fx.LaunchShell(from, to, e.Value, e.Value2 > 0f, rocket);
                        _ = targetUnit;
                        break;
                    }
                    case SimEventType.Impact:
                    {
                        if (!NearCamera(e.Pos, 700f)) break;
                        Vector3 p = e.Pos.ToV3(Mathf.Max(map.GroundHeight(e.Pos), map.WaterLevel) + (e.Value2 > 0f ? 30f : 0.3f));
                        if (e.Value >= 2.5f || e.Damage == DamageType.HE) Fx.Explosion(p, Mathf.Max(2f, e.Value), false);
                        else Fx.Explosion(p, 1.2f, false);
                        break;
                    }
                    case SimEventType.Explosion:
                    {
                        Vector3 p = e.Pos.ToV3(Mathf.Max(map.GroundHeight(e.Pos), map.WaterLevel) + 0.3f);
                        Fx.Explosion(p, Mathf.Max(2f, e.Value), e.Value2 >= 2f);
                        break;
                    }
                    case SimEventType.UnitDied:
                    {
                        var def = Sim.Units.Get(e.DefId);
                        if (def == null) break;
                        Vector3 p = e.Pos.ToV3(map.GroundHeight(e.Pos) + 1f);
                        if (def.IsVehicle) Fx.Explosion(p, 4.5f, true);
                        else if (def.IsStructure) Fx.Explosion(p, def.IsHQ ? 25f : 7f, true);
                        if (player && e.Team == PlayerTeam && def.IsStructure && !def.IsHQ) Notify("아군 건물 파괴: " + def.NameKo, UiTheme.WarnRed, e.Pos);
                        if (player && e.Team != PlayerTeam && def.IsStructure && !def.IsHQ) Notify("적 건물 격파: " + def.NameKo, UiTheme.Green, e.Pos);
                        break;
                    }
                    case SimEventType.ZoneOwnerChanged:
                    {
                        var z = Sim.Zones[e.UnitId];
                        string name = z.Def.Id + " " + z.Def.Name;
                        if (e.Team == PlayerTeam) { if (player) Notify("구역 " + name + " 점령!", UiTheme.MilBlue, z.Def.Center); Ping(z.Def.Center, UiTheme.MilBlue); }
                        else if (e.Team == TeamUtil.Opponent(PlayerTeam)) { if (player) Notify("적군이 구역 " + name + " 점령", UiTheme.WarnRed, z.Def.Center); Ping(z.Def.Center, UiTheme.WarnRed); }
                        else if ((Team)e.OtherId == PlayerTeam && player) Notify("구역 " + name + " 상실 (중립화)", UiTheme.WarnRed, z.Def.Center);
                        break;
                    }
                    case SimEventType.BuildStarted:
                        Fx.Dust(e.Pos.ToV3(map.SampleHeight(e.Pos) + 1f), 6f);
                        break;
                    case SimEventType.BuildCompleted:
                        if (player && e.Team == PlayerTeam) Notify("건설 완료: " + Sim.Units.Get(e.DefId)?.NameKo, UiTheme.Green, e.Pos);
                        break;
                    case SimEventType.RubbleCreated:
                        Fx.Explosion(e.Pos.ToV3(map.SampleHeight(e.Pos) + 1f), 5f, false);
                        break;
                    case SimEventType.RubbleCleared:
                        if (player && e.Team == PlayerTeam) Notify("잔해 철거 완료 - 재건설 가능", UiTheme.Green, e.Pos);
                        Fx.Dust(e.Pos.ToV3(map.SampleHeight(e.Pos) + 1f), 6f);
                        break;
                    case SimEventType.Heal:
                        if (!NearCamera(e.Pos, 300f)) break;
                        Fx.Beam(CenterOf(e.UnitId, e.Pos), CenterOf(e.OtherId, e.Pos2), new Color(0.4f, 1f, 0.5f, 0.8f), 0.3f, 0.08f);
                        Fx.HealSparkle(CenterOf(e.OtherId, e.Pos2), new Color(0.5f, 1f, 0.6f, 1f));
                        break;
                    case SimEventType.Repair:
                        if (!NearCamera(e.Pos, 300f)) break;
                        Fx.HealSparkle(e.Pos2.ToV3(map.GroundHeight(e.Pos2) + 1.2f), new Color(1f, 0.8f, 0.3f, 1f));
                        break;
                    case SimEventType.AbilityUsed:
                    {
                        var def = Sim.Units.Get(e.DefId);
                        if (def == null) break;
                        if (def.Ability == AbilityType.CallArtillery)
                        {
                            Fx.MissileWarning(e.Pos.ToV3(map.GroundHeight(e.Pos)), 9f, e.Value + 1.5f);
                            if (player && e.Team == PlayerTeam) Notify("무전병: 포격 요청!", UiTheme.Gold, e.Pos);
                        }
                        else if (def.Ability == AbilityType.Paradrop && player && e.Team == PlayerTeam) Notify("공수부대 강하 개시", UiTheme.MilBlue, e.Pos);
                        else if (def.Ability == AbilityType.Charge && NearCamera(e.Pos, 300f)) Fx.Dust(e.Pos.ToV3(map.GroundHeight(e.Pos)), 2f);
                        break;
                    }
                    case SimEventType.AirstrikeFlyby:
                    {
                        var def = Sim.Units.Get(e.DefId);
                        if (def == null) break;
                        int kind = def.Ability == AbilityType.SupplyDrop || def.Ability == AbilityType.CargoDrop ? 2 : 1;
                        Fx.Flyover(def, e.Team, e.Pos, e.Pos2, Mathf.Max(1.5f, e.Value), kind);
                        if (kind == 1) Fx.MissileWarning(e.Pos.ToV3(map.GroundHeight(e.Pos)), Mathf.Max(8f, def.AbRadius), e.Value + 0.5f);
                        if (player && e.Team != PlayerTeam && kind == 1) Notify("경고: 적 공습 접근!", UiTheme.WarnRed, e.Pos);
                        break;
                    }
                    case SimEventType.MissileWarning:
                        Fx.MissileWarning(e.Pos.ToV3(map.GroundHeight(e.Pos)), e.Value2, e.Value);
                        Ping(e.Pos, UiTheme.WarnRed);
                        break;
                    case SimEventType.MineTriggered:
                        Fx.Explosion(e.Pos.ToV3(map.GroundHeight(e.Pos) + 0.2f), 3.5f, false);
                        break;
                    case SimEventType.MineCleared:
                        Fx.HealSparkle(e.Pos.ToV3(map.GroundHeight(e.Pos) + 0.3f), new Color(1f, 0.9f, 0.4f, 1f));
                        break;
                    case SimEventType.TeslaArc:
                        if (NearCamera(e.Pos, 500f)) Fx.TeslaArc(CenterOf(e.UnitId, e.Pos) + Vector3.up * 2f, CenterOf(e.OtherId, e.Pos2));
                        break;
                    case SimEventType.CpGained:
                        if (player && e.Team == PlayerTeam) Notify("보급 투하: CP +" + e.Value.ToString("0.#"), UiTheme.Gold, e.Pos);
                        break;
                    case SimEventType.DeployFailed:
                        if (player && e.Team == PlayerTeam && Cmd != null) Cmd.Say(e.Text);
                        break;
                    case SimEventType.Notification:
                        if (player && (e.Team == PlayerTeam || e.Team == Team.Neutral)) Notify(e.Text, UiTheme.WarnRed, e.Pos);
                        break;
                    case SimEventType.Dismount:
                        if (player && e.Team == PlayerTeam) Notify("병력 하차 (" + (int)e.Value + "명)", UiTheme.MilBlue, e.Pos);
                        break;
                    case SimEventType.GameOver:
                        if (player) Notify(e.Team == PlayerTeam ? "적 본진 파괴! 승리!" : "아군 본진 함락...", e.Team == PlayerTeam ? UiTheme.Gold : UiTheme.WarnRed);
                        break;
                }
            }
            Sim.Events.Clear();

            // HQ under attack warning.
            if (player)
            {
                var hq = Sim.Hq(PlayerTeam);
                if (hq != null)
                {
                    if (hq.Hp < _lastHqHp - 1f && Time.unscaledTime - _lastHqWarn > 12f)
                    {
                        _lastHqWarn = Time.unscaledTime;
                        Notify("경고: 아군 본진이 공격받고 있습니다!", UiTheme.WarnRed, hq.Pos);
                        Ping(hq.Pos, UiTheme.WarnRed);
                    }
                    _lastHqHp = hq.Hp;
                }
            }
        }

        private void RecordResult()
        {
            ResultRecorded = true;
            var ts = Sim.TeamOf(PlayerTeam);
            bool win = Sim.Winner == PlayerTeam;
            int gold = 120 + ts.Stats.Kills * 3 + ts.Stats.ZonesCaptured * 15 + (win ? 400 : 0);
            int xp = 200 + ts.Stats.Kills * 4 + ts.Stats.ZonesCaptured * 20 + (win ? 500 : 0);
            Result = new MatchRecord
            {
                date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                victory = win,
                duration = Sim.Time,
                kills = ts.Stats.Kills,
                losses = ts.Stats.Losses,
                zones = Sim.ZonesOwned(PlayerTeam),
                buildings = ts.Stats.StructuresBuilt,
                difficulty = Sim.Setup.Difficulty,
                goldReward = gold,
                xpReward = xp,
            };
            var p = Root.Profile;
            p.gold += gold;
            p.AddXp(xp);
            p.totalBattles++;
            if (win) p.totalWins++;
            p.records.Insert(0, Result);
            if (p.records.Count > 50) p.records.RemoveAt(p.records.Count - 1);
            p.Save();
        }

        public void Surrender()
        {
            if (Sim == null || IsDemo) return;
            var hq = Sim.Hq(PlayerTeam);
            if (hq != null) Sim.Kill(hq, null);
            Paused = false;
        }
    }
}
