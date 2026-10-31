using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed partial class BattleSimulation
    {
        // ------------------------------------------------------------------ CP

        public float CpRegenRate(Team team)
        {
            var ts = Teams[(int)team];
            float rate = Config.CpRegenPerSecond * ts.CpRegenMultiplier;
            if (ts.ExtractorCount > 0)
                rate *= Config.ExtractorStacks ? MathF.Pow(Config.ExtractorMultiplier, ts.ExtractorCount) : Config.ExtractorMultiplier;
            float bonus = ts.ForwardPostBonus + ts.ResupplyBonus;
            var strategic = ZoneById(Config.StrategicZoneId);
            if (strategic != null && strategic.Owner == team) bonus += Config.StrategicZoneRegenBonus;
            rate *= 1f + bonus;
            if (ts.CargoBoostTimer > 0f) rate *= ts.CargoBoostMultiplier;
            return rate;
        }

        private void UpdateCp(float dt)
        {
            for (int t = 1; t <= 2; t++)
            {
                var ts = Teams[t];
                if (ts.CargoBoostTimer > 0f) ts.CargoBoostTimer -= dt;
                ts.LastRegenRate = CpRegenRate((Team)t);
                AddCp((Team)t, ts.LastRegenRate * dt);
                if (ts.CardCooldowns.Count > 0)
                {
                    var keys = new List<int>(ts.CardCooldowns.Keys);
                    foreach (int k in keys)
                    {
                        float v = ts.CardCooldowns[k] - dt;
                        if (v <= 0f) ts.CardCooldowns.Remove(k);
                        else ts.CardCooldowns[k] = v;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ deployment

        /// <summary>Is the point inside friendly territory where ground units may be deployed?</summary>
        public bool IsDeployableArea(Team team, Vec2 pos)
        {
            var hq = Map.Layout.GetHq(team);
            if (hq != null && Vec2.Distance(hq.Position, pos) <= Config.HqDeployRadius) return true;
            foreach (var z in Zones)
                if (z.Owner == team && Vec2.Distance(z.Def.Center, pos) <= z.Def.Radius * Config.ZoneDeployRadiusFactor) return true;
            foreach (var u in UnitList)
                if (u.Alive && u.Team == team && u.Def.Ability == AbilityType.ForwardPost && !u.Constructing &&
                    Vec2.Distance(u.Pos, pos) <= u.Def.AbRadius) return true;
            return false;
        }

        /// <summary>Validates a card deployment without changing state (used by the HUD for previews).</summary>
        public bool CanDeploy(Team team, int defId, Vec2 pos, int padId, out string reason)
        {
            reason = "";
            if (IsOver) { reason = "전투 종료"; return false; }
            var def = Units.Get(defId);
            var ts = Teams[(int)team];
            if (def == null) { reason = "알 수 없는 카드"; return false; }
            if (!ts.HasCard(defId)) { reason = "덱에 없는 카드"; return false; }
            if (ts.Cp + 1e-4f < def.CpCost) { reason = "CP 부족 (" + def.CpCost + " 필요)"; return false; }
            if (ts.CardCooldown(defId) > 0f) { reason = "재사용 대기 " + MathF.Ceiling(ts.CardCooldown(defId)) + "초"; return false; }
            if (!def.IsSkill && CountUnits(team) + def.Count > Config.MaxUnitsPerTeam) { reason = "병력 한도 초과"; return false; }

            if (def.IsStructure)
            {
                var pad = padId >= 0 && padId < Pads.Length ? Pads[padId] : PadNear(pos, 12f);
                if (pad == null) { reason = "건설 패드를 선택하세요"; return false; }
                var zone = Zones[pad.ZoneIndex];
                if (zone.Owner != team) { reason = "점령한 구역의 패드에만 건설 가능"; return false; }
                if (pad.State == PadState.Rubble) { reason = "잔해 철거 필요 (공병)"; return false; }
                if (pad.State == PadState.Occupied) { reason = "이미 건물이 있는 패드"; return false; }
                if (StructuresInZone(pad.ZoneIndex) >= Config.MaxBuildingsPerZone) { reason = "구역당 건설 한도 " + Config.MaxBuildingsPerZone + "개"; return false; }
                return true;
            }
            if (def.IsSkill || def.IsAir)
            {
                if (Math.Abs(pos.X) > Map.Half - 5f || Math.Abs(pos.Z) > Map.Half - 5f) { reason = "맵 밖"; return false; }
                return true;
            }
            // ground
            if (!Map.Nav.NearestWalkable(pos, def.Has(UnitFlags.Amphibious), 3, out _)) { reason = "배치할 수 없는 지형"; return false; }
            if (def.Has(UnitFlags.DeployAnywhere))
            {
                var enemyHq = Map.Layout.GetHq(TeamUtil.Opponent(team));
                if (!Map.InPlayableArea(pos)) { reason = "전장 밖"; return false; }
                if (enemyHq != null && Vec2.Distance(enemyHq.Position, pos) < Config.ParadropMinDistFromEnemyHQ) { reason = "적 본진 대공망 (강하 불가)"; return false; }
                return true;
            }
            if (!IsDeployableArea(team, pos)) { reason = "아군 구역·본진·전방 지휘소 근처에만 배치"; return false; }
            return true;
        }

        public bool TryDeploy(Team team, int defId, Vec2 pos, int padId, out string reason)
        {
            if (!CanDeploy(team, defId, pos, padId, out reason)) return false;
            var def = Units.Get(defId);
            var ts = Teams[(int)team];
            AddCp(team, -def.CpCost);
            ts.Stats.CpSpent += def.CpCost;
            if (def.CardCooldown > 0f) ts.CardCooldowns[defId] = def.CardCooldown;
            ts.Stats.UnitsDeployed += def.IsSkill ? 0 : def.Count;
            Events.Add(new SimEvent { Type = SimEventType.CardDeployed, Team = team, DefId = defId, Pos = pos });

            if (def.IsStructure)
            {
                var pad = padId >= 0 && padId < Pads.Length ? Pads[padId] : PadNear(pos, 12f);
                var s = SpawnUnit(def, team, pad.Pos, team == Team.Player ? 0f : MathUtil.Pi);
                s.PadId = pad.Id;
                s.Constructing = def.BuildTime > 0f;
                s.BuildProgress = s.Constructing ? 0f : 1f;
                if (s.Constructing) s.Hp = s.MaxHp * 0.1f;
                pad.State = PadState.Occupied;
                pad.StructureId = s.Id;
                Events.Add(new SimEvent { Type = SimEventType.BuildStarted, Team = team, UnitId = s.Id, DefId = defId, Pos = pad.Pos, Value = def.BuildTime });
                return true;
            }
            if (def.IsSkill)
            {
                ExecuteSkill(team, def, pos);
                return true;
            }
            if (def.IsAir)
            {
                var hq = Map.Layout.GetHq(team);
                Vec2 origin = hq != null ? hq.Position + new Vec2(0f, team == Team.Player ? -40f : 40f) : pos;
                var units = SpawnSquad(def, team, origin, false);
                foreach (var u in units)
                {
                    u.Yaw = (pos - origin).ToYaw();
                    u.PlayerControlled = team == Team.Player && !Setup.PlayerAi;
                    u.Anchor = pos + Rng.InsideCircle(6f);
                    if (def.Ability == AbilityType.Transport)
                    {
                        u.PassengerCount = (int)def.AbP2;
                        u.Order = OrderType.Move;
                        u.OrderPos = pos;
                    }
                    else if (def.Ability == AbilityType.Suicide)
                    {
                        u.Order = OrderType.AttackMove;
                        u.OrderPos = pos + Rng.InsideCircle(8f);
                    }
                    else
                    {
                        u.Order = OrderType.AttackMove;
                        u.OrderPos = u.Anchor;
                    }
                }
                return true;
            }

            // Ground squad.
            var squad = SpawnSquad(def, team, pos, false);
            foreach (var u in squad)
            {
                u.PlayerControlled = team == Team.Player && !Setup.PlayerAi;
                u.Order = OrderType.Idle;
                u.Anchor = u.Pos;
                if (def.Ability == AbilityType.Paradrop) u.DropTimer = Math.Max(0.5f, def.AbP1);
                if (def.Ability == AbilityType.Transport) u.PassengerCount = (int)def.AbP2;
            }
            if (def.Ability == AbilityType.Paradrop)
                Events.Add(new SimEvent { Type = SimEventType.AbilityUsed, Team = team, DefId = defId, Pos = pos, Value = def.AbP1 });
            _lastDeployed.Clear();
            _lastDeployed.AddRange(squad);
            return true;
        }

        private readonly List<SimUnit> _lastDeployed = new List<SimUnit>();
        /// <summary>Units created by the most recent ground deployment (used by the AI to give lane orders).</summary>
        public IReadOnlyList<SimUnit> LastDeployed => _lastDeployed;

        private void ExecuteSkill(Team team, UnitDefinition def, Vec2 pos)
        {
            var hq = Map.Layout.GetHq(team);
            Vec2 from = hq != null ? hq.Position : new Vec2(0, team == Team.Player ? -600 : 600);
            Vec2 dir = (pos - from).Normalized;
            switch (def.Ability)
            {
                case AbilityType.Airstrike:
                    ScheduleStrike(team, pos, def.AbP2, def.AbRadius, def.AbP1, DamageType.HE, def.Id, true);
                    Events.Add(new SimEvent { Type = SimEventType.AirstrikeFlyby, Team = team, DefId = def.Id, Pos = pos, Pos2 = dir, Value = def.AbP2 });
                    break;
                case AbilityType.CarpetBomb:
                {
                    int bombs = Math.Max(1, (int)def.AbP2);
                    float spacing = 8f;
                    for (int i = 0; i < bombs; i++)
                    {
                        float off = (i - (bombs - 1) * 0.5f) * spacing;
                        ScheduleStrike(team, pos + dir * off, 3f + i * 0.18f, def.AbRadius, def.AbP1, DamageType.HE, def.Id);
                    }
                    Events.Add(new SimEvent { Type = SimEventType.AirstrikeFlyby, Team = team, DefId = def.Id, Pos = pos, Pos2 = dir, Value = 3f });
                    break;
                }
                case AbilityType.Missile:
                    ScheduleStrike(team, pos, def.AbP2, def.AbRadius, def.AbP1, DamageType.HE, def.Id, true);
                    Events.Add(new SimEvent { Type = SimEventType.MissileWarning, Team = team, DefId = def.Id, Pos = pos, Value = def.AbP2, Value2 = def.AbRadius });
                    Notify(TeamUtil.Opponent(team), "경고: V2 미사일 접근!", pos);
                    break;
                case AbilityType.SupplyDrop:
                {
                    // Spec: supply drop grants +3 CP (clamped to 0..10).
                    float gained = AddCp(team, Config.SupplyDropAmount);
                    Events.Add(new SimEvent { Type = SimEventType.CpGained, Team = team, DefId = def.Id, Pos = pos, Value = gained });
                    Events.Add(new SimEvent { Type = SimEventType.AirstrikeFlyby, Team = team, DefId = def.Id, Pos = pos, Pos2 = dir, Value = 2f });
                    _abilityList.Clear();
                    Spatial.Query(pos, def.AbRadius, _abilityList);
                    foreach (var o in _abilityList)
                        if (o.Team == team && !o.IsStructure) o.Hp = Math.Min(o.MaxHp, o.Hp + def.AbP2);
                    break;
                }
                case AbilityType.CargoDrop:
                {
                    var ts = Teams[(int)team];
                    ts.CargoBoostTimer = Math.Max(ts.CargoBoostTimer, def.AbP2);
                    ts.CargoBoostMultiplier = Math.Max(1f, def.AbP1);
                    Events.Add(new SimEvent { Type = SimEventType.AirstrikeFlyby, Team = team, DefId = def.Id, Pos = pos, Pos2 = dir, Value = 2f });
                    Notify(team, "화물 수송선: CP 충전 속도 x" + def.AbP1.ToString("0.0") + " (" + def.AbP2 + "초)", pos);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ lane waves (기본 AI 부대 진격)

        private void UpdateWaves(float dt)
        {
            _waveTimer -= dt;
            if (_waveTimer > 0f) return;
            _waveTimer = Config.WaveInterval;
            _waveCount++;

            var comp = new List<int>(Config.WaveBase);
            if (_waveCount % 3 == 0) comp.AddRange(Config.WaveEvery3rd);
            if (_waveCount % 4 == 0) comp.AddRange(Config.WaveEvery4th);

            foreach (Team team in new[] { Team.Player, Team.Enemy })
            {
                int waveAlive = 0;
                foreach (var u in UnitList) if (u.Alive && u.Team == team && u.IsWaveUnit) waveAlive++;
                for (int lane = 0; lane < Map.Lanes.Count; lane++)
                {
                    Vec2 gate = HqGatePosition(team, lane, 14f);
                    int i = 0;
                    foreach (int defId in comp)
                    {
                        if (waveAlive >= Config.MaxWaveUnitsPerTeam || CountUnits(team) >= Config.MaxUnitsPerTeam) break;
                        var def = Units.Get(defId);
                        if (def == null) continue;
                        Vec2 p = gate + new Vec2((i % 3 - 1) * 2.6f, (i / 3) * 2.6f);
                        Map.Nav.NearestWalkable(p, false, 6, out p);
                        var u = SpawnUnit(def, team, p, team == Team.Player ? 0f : MathUtil.Pi);
                        u.IsWaveUnit = true;
                        AssignLane(u, lane);
                        waveAlive++;
                        i++;
                    }
                }
            }
        }

        public float NextWaveIn => _waveTimer;
        public int WaveNumber => _waveCount;

        // ------------------------------------------------------------------ capture

        private void UpdateCapture(float dt)
        {
            foreach (var z in Zones)
            {
                int p = 0, e = 0;
                _abilityList.Clear();
                Spatial.Query(z.Def.Center, z.Def.Radius, _abilityList);
                foreach (var u in _abilityList)
                {
                    if (!u.IsGround || !u.Targetable) continue;
                    if (u.Team == Team.Player) p++;
                    else if (u.Team == Team.Enemy) e++;
                }
                z.PlayerCount = p;
                z.EnemyCount = e;
                z.Contested = p > 0 && e > 0;
                Team before = z.Owner;

                if (z.Contested)
                {
                    z.Capturer = Team.Neutral;
                }
                else if (p > 0 || e > 0)
                {
                    Team t = p > 0 ? Team.Player : Team.Enemy;
                    int n = p > 0 ? p : e;
                    float speed = Math.Min(Config.MaxCaptureSpeedMultiplier, 1f + Config.PerExtraUnitBonus * (n - 1));
                    float delta = dt / Config.SecondsToCapture * speed * (t == Team.Player ? 1f : -1f);
                    float prev = z.Control;
                    z.Control = MathUtil.Clamp(z.Control + delta, -1f, 1f);
                    z.Capturer = Math.Abs(z.Control) >= 1f && z.Owner == t ? Team.Neutral : t;

                    if (z.Owner != Team.Neutral && z.Owner != t)
                    {
                        // Neutralize first.
                        if ((z.Owner == Team.Player && z.Control <= 0f) || (z.Owner == Team.Enemy && z.Control >= 0f))
                        {
                            z.Owner = Team.Neutral;
                            z.Control = 0f;
                        }
                    }
                    if (z.Owner == Team.Neutral && Math.Abs(z.Control) >= 1f) z.Owner = t;
                    if (prev == z.Control) z.Capturer = Team.Neutral;
                }
                else
                {
                    z.Capturer = Team.Neutral;
                    float target = z.Owner == Team.Player ? 1f : (z.Owner == Team.Enemy ? -1f : 0f);
                    float step = Config.AbandonRevertPerSecond * dt;
                    if (z.Control < target) z.Control = Math.Min(target, z.Control + step);
                    else if (z.Control > target) z.Control = Math.Max(target, z.Control - step);
                }

                if (z.Owner != before)
                {
                    if (z.Owner != Team.Neutral) Teams[(int)z.Owner].Stats.ZonesCaptured++;
                    Events.Add(new SimEvent { Type = SimEventType.ZoneOwnerChanged, Team = z.Owner, UnitId = z.Index, OtherId = (int)before, Pos = z.Def.Center, Text = z.Def.Name });
                }
            }
        }
    }
}
