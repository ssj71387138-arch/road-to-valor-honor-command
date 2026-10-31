using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed partial class BattleSimulation
    {
        private readonly List<SimUnit> _abilityList = new List<SimUnit>(64);

        /// <summary>Per-tick ability logic for units that act on their own (heal, repair, mines, drones, artillery calls...).</summary>
        private void UpdateAbility(SimUnit u, float dt)
        {
            var d = u.Def;
            if (d.Ability == AbilityType.None || u.Constructing) return;
            switch (d.Ability)
            {
                case AbilityType.Charge:
                {
                    if (u.AbilityCooldown > 0f || u.ChargeTimer > 0f) break;
                    if (u.PlayerControlled && d.AbilityMode == AbilityMode.Active) break;
                    var t = Get(u.TargetId);
                    if (t != null && t.Alive && !InRange(u, t) && Vec2.Distance(u.Pos, t.Pos) < d.Sight + 10f) ActivateCharge(u);
                    break;
                }
                case AbilityType.Heal:
                {
                    u.AbilityTick -= dt;
                    if (u.AbilityTick > 0f) break;
                    u.AbilityTick = 0.5f;
                    _abilityList.Clear();
                    Spatial.Query(u.Pos, d.AbRadius, _abilityList);
                    SimUnit best = null;
                    float bestRatio = 0.999f;
                    foreach (var o in _abilityList)
                    {
                        if (o.Team != u.Team || !o.IsInfantry || !o.Targetable) continue;
                        float r = o.HpRatio;
                        if (r < bestRatio) { bestRatio = r; best = o; }
                    }
                    if (best != null)
                    {
                        best.Hp = Math.Min(best.MaxHp, best.Hp + d.AbP1 * 0.5f);
                        Events.Add(new SimEvent { Type = SimEventType.Heal, Team = u.Team, UnitId = u.Id, OtherId = best.Id, Pos = u.Pos, Pos2 = best.Pos });
                        if (u.TargetId == 0) { u.Activity = UnitActivity.Ability; u.ActivityTimer = 0.5f; }
                    }
                    break;
                }
                case AbilityType.Repair:
                {
                    u.AbilityTick -= dt;
                    if (u.AbilityTick > 0f) break;
                    u.AbilityTick = 0.5f;
                    _abilityList.Clear();
                    Spatial.Query(u.Pos, d.AbRadius + 4f, _abilityList);
                    SimUnit best = null;
                    float bestRatio = 0.999f;
                    foreach (var o in _abilityList)
                    {
                        if (o.Team != u.Team || o.IsInfantry || o.IsAir || o == u || !o.Alive) continue;
                        float r = o.HpRatio;
                        if (r < bestRatio) { bestRatio = r; best = o; }
                    }
                    bool worked = false;
                    if (best != null)
                    {
                        best.Hp = Math.Min(best.MaxHp, best.Hp + d.AbP1 * 0.5f);
                        if (best.Constructing) best.BuildProgress = Math.Min(1f, best.BuildProgress + 0.04f);
                        Events.Add(new SimEvent { Type = SimEventType.Repair, Team = u.Team, UnitId = u.Id, OtherId = best.Id, Pos = u.Pos, Pos2 = best.Pos });
                        worked = true;
                    }
                    else
                    {
                        // Clear building rubble so the pad can be rebuilt.
                        BuildPad pad = null;
                        float bd = (d.AbRadius + 6f) * (d.AbRadius + 6f);
                        foreach (var p in Pads)
                        {
                            if (p.State != PadState.Rubble) continue;
                            float dd = Vec2.DistanceSq(p.Pos, u.Pos);
                            if (dd < bd) { bd = dd; pad = p; }
                        }
                        if (pad != null)
                        {
                            pad.RubbleWork -= d.AbP2 * 0.5f;
                            Events.Add(new SimEvent { Type = SimEventType.Repair, Team = u.Team, UnitId = u.Id, OtherId = 0, Pos = u.Pos, Pos2 = pad.Pos });
                            worked = true;
                            if (pad.RubbleWork <= 0f) ClearRubble(pad, u.Team);
                        }
                    }
                    if (worked && u.TargetId == 0 && u.Velocity.SqrLength < 0.1f) { u.Activity = UnitActivity.Ability; u.ActivityTimer = 0.5f; }
                    break;
                }
                case AbilityType.MineSweep:
                {
                    u.AbilityTick -= dt;
                    if (u.AbilityTick > 0f) break;
                    u.AbilityTick = 0.5f;
                    foreach (var m in Mines)
                    {
                        if (!m.Alive || m.Team == u.Team) continue;
                        float dist = Vec2.Distance(m.Pos, u.Pos);
                        if (dist <= d.AbRadius) m.Revealed = true;
                        if (dist <= d.AbRadius * 0.6f)
                        {
                            m.Alive = false;
                            Events.Add(new SimEvent { Type = SimEventType.MineCleared, Team = u.Team, UnitId = m.Id, Pos = m.Pos });
                            if (u.TargetId == 0) { u.Activity = UnitActivity.Ability; u.ActivityTimer = 0.8f; }
                            break;
                        }
                    }
                    break;
                }
                case AbilityType.LayMines:
                {
                    if (u.AbilityCooldown > 0f) break;
                    u.AbilityCooldown = Math.Max(1f, d.AbCooldown);
                    int mine = 0, teamMines = 0;
                    foreach (var m in Mines)
                    {
                        if (!m.Alive || m.Team != u.Team) continue;
                        teamMines++;
                        if (m.OwnerId == u.Id) mine++;
                    }
                    if (mine >= (int)d.AbP2 || teamMines >= Config.MaxMinesPerTeam) break;
                    var own = Hq(u.Team);
                    if (own != null && Vec2.Distance(own.Pos, u.Pos) < 90f) break;
                    Vec2 back = u.Pos - Vec2.FromYaw(u.Yaw) * (u.Radius + 2.5f);
                    if (!Map.Nav.IsWalkable(back, false)) back = u.Pos;
                    var mineObj = new Mine
                    {
                        Id = _nextMineId++,
                        Team = u.Team,
                        Pos = back,
                        Damage = d.AbP1,
                        TriggerRadius = Math.Max(2f, d.AbRadius),
                        AntiTankOnly = d.Has(UnitFlags.AtMine),
                        OwnerId = u.Id,
                    };
                    Mines.Add(mineObj);
                    Events.Add(new SimEvent { Type = SimEventType.MineLaid, Team = u.Team, UnitId = mineObj.Id, OtherId = u.Id, Pos = back, Value = mineObj.AntiTankOnly ? 1f : 0f });
                    break;
                }
                case AbilityType.DroneSwarm:
                {
                    if (u.AbilityCooldown > 0f) break;
                    var droneDef = Units.Get((int)d.AbP1);
                    if (droneDef == null) break;
                    int alive = 0;
                    foreach (var o in UnitList) if (o.Alive && o.OwnerUnitId == u.Id) alive++;
                    foreach (var o in _spawnBuffer) if (o.OwnerUnitId == u.Id) alive++;
                    if (alive >= (int)d.AbP2) break;
                    // Only launch when enemies are around.
                    if (Spatial.Count(u.Pos, d.AbRadius, TeamUtil.Opponent(u.Team), true) == 0) break;
                    u.AbilityCooldown = Math.Max(2f, d.AbCooldown);
                    var drone = SpawnUnit(droneDef, u.Team, u.Pos + Vec2.FromYaw(u.Yaw) * 2f, u.Yaw, true);
                    drone.OwnerUnitId = u.Id;
                    drone.Order = OrderType.Idle;
                    drone.Anchor = u.Pos;
                    drone.Lifetime = Math.Min(drone.Lifetime > 0f ? drone.Lifetime : 20f, 20f);
                    Events.Add(new SimEvent { Type = SimEventType.AbilityUsed, Team = u.Team, UnitId = u.Id, DefId = d.Id, Pos = u.Pos });
                    u.Activity = UnitActivity.Ability;
                    u.ActivityTimer = 0.8f;
                    break;
                }
                case AbilityType.CallArtillery:
                {
                    if (u.AbilityCooldown > 0f) break;
                    _abilityList.Clear();
                    Spatial.Query(u.Pos, d.AbRadius, _abilityList);
                    SimUnit bestT = null;
                    int bestN = 0;
                    foreach (var o in _abilityList)
                    {
                        if (o.Team == u.Team || o.Team == Team.Neutral || !o.Targetable || o.IsAir) continue;
                        if (o.Def.Ability == AbilityType.Stealth && !o.Revealed) continue;
                        int n = Spatial.Count(o.Pos, 10f, o.Team, true);
                        if (n > bestN) { bestN = n; bestT = o; }
                    }
                    if (bestT == null || bestN < 3) { u.AbilityCooldown = 2f; break; }
                    u.AbilityCooldown = Math.Max(3f, d.AbCooldown);
                    int shells = Math.Max(1, (int)d.AbP2);
                    for (int i = 0; i < shells; i++)
                        ScheduleStrike(u.Team, bestT.Pos + Rng.InsideCircle(6f), 2.5f + i * 0.6f, 7f, d.AbP1, DamageType.HE, d.Id);
                    Events.Add(new SimEvent { Type = SimEventType.AbilityUsed, Team = u.Team, UnitId = u.Id, DefId = d.Id, Pos = bestT.Pos, Value = 2.5f });
                    u.Activity = UnitActivity.Ability;
                    u.ActivityTimer = 1.2f;
                    break;
                }
                case AbilityType.Transport:
                {
                    if (u.PassengerCount <= 0) break;
                    if (u.PlayerControlled && !u.IsAir) break; // player decides (ability button / arrival)
                    if (Spatial.Count(u.Pos, 32f, TeamUtil.Opponent(u.Team), true) > 0) Dismount(u, 1f);
                    break;
                }
            }
        }

        private void ActivateCharge(SimUnit u)
        {
            u.ChargeTimer = Math.Max(1f, u.Def.AbP2);
            u.AbilityCooldown = Math.Max(1f, u.Def.AbCooldown);
            u.Activity = UnitActivity.Ability;
            u.ActivityTimer = 0.4f;
            Events.Add(new SimEvent { Type = SimEventType.AbilityUsed, Team = u.Team, UnitId = u.Id, DefId = u.Def.Id, Pos = u.Pos });
        }

        /// <summary>Manual trigger from the HUD ability button.</summary>
        public bool TriggerActiveAbility(SimUnit u)
        {
            if (u == null || !u.Alive) return false;
            switch (u.Def.Ability)
            {
                case AbilityType.Charge:
                    if (u.AbilityCooldown > 0f || u.ChargeTimer > 0f) return false;
                    ActivateCharge(u);
                    return true;
                case AbilityType.Transport:
                    if (u.PassengerCount <= 0) return false;
                    Dismount(u, 1f);
                    return true;
                case AbilityType.Suicide:
                    if (u.IsAir) return false;
                    Explode(u);
                    return true;
            }
            return false;
        }

        public static bool HasActiveAbility(UnitDefinition d)
        {
            switch (d.Ability)
            {
                case AbilityType.Charge:
                case AbilityType.Transport:
                case AbilityType.Suicide:
                    return d.IsGroundMobile || d.Ability == AbilityType.Transport;
                default:
                    return false;
            }
        }

        public void Dismount(SimUnit u, float hpFraction)
        {
            var pdef = Units.Get((int)u.Def.AbP1);
            int n = u.PassengerCount;
            u.PassengerCount = 0;
            if (pdef == null || n <= 0) return;
            Vec2 basePos = u.Pos - Vec2.FromYaw(u.Yaw) * (u.Radius + 2f);
            for (int i = 0; i < n; i++)
            {
                Vec2 p = basePos + new Vec2((i % 3 - 1) * 2.4f, -(i / 3) * 2.4f).RotateYaw(u.Yaw);
                Map.Nav.NearestWalkable(p, false, 8, out p);
                var s = SpawnUnit(pdef, u.Team, p, u.Yaw, true);
                s.Hp = s.MaxHp * hpFraction;
                s.PlayerControlled = u.PlayerControlled;
                if (u.LaneIndex >= 0 && !u.PlayerControlled)
                {
                    s.Order = OrderType.Lane;
                    s.LaneIndex = u.LaneIndex;
                    s.LaneDir = u.LaneDir;
                    s.LaneProgress = Map.Lanes[u.LaneIndex].NearestIndex(p);
                }
                else
                {
                    s.Order = OrderType.Idle;
                    s.Anchor = p;
                }
            }
            Events.Add(new SimEvent { Type = SimEventType.Dismount, Team = u.Team, UnitId = u.Id, DefId = pdef.Id, Pos = u.Pos, Value = n });

            // Helicopters head home and leave after dropping troops.
            if (u.IsAir && u.Alive)
            {
                var hq = Map.Layout.GetHq(u.Team);
                u.Order = OrderType.Move;
                u.OrderPos = hq != null ? hq.Position : u.Pos;
                u.Lifetime = 10f;
                u.Exiting = true;
            }
        }

        private void ClearRubble(BuildPad pad, Team by)
        {
            pad.State = PadState.Empty;
            pad.RubbleWork = 0f;
            Events.Add(new SimEvent { Type = SimEventType.RubbleCleared, Team = by, UnitId = pad.Id, Pos = pad.Pos });
        }

        private void UpdateRubble(float dt)
        {
            if (Config.RubbleAutoClearSeconds <= 0f) return;
            foreach (var pad in Pads)
                if (pad.State == PadState.Rubble && Time - pad.RubbleTime > Config.RubbleAutoClearSeconds) ClearRubble(pad, Team.Neutral);
        }

        // ------------------------------------------------------------------ fields (auras, spotting, stealth, team bonuses)

        private void UpdateFields()
        {
            foreach (var u in UnitList)
            {
                u.AuraAttackBonus = 0f;
                u.AuraDefense = 0f;
                u.FearAttackMult = 1f;
                u.FearSpeedMult = 1f;
                u.SlowMult = 1f;
                u.FlareBonus = 0f;
                u.SpottedBy[1] = false;
                u.SpottedBy[2] = false;
                u.Revealed = u.Def.Ability != AbilityType.Stealth || (Time - u.LastFireTime) < u.Def.AbP2;
            }
            for (int t = 1; t <= 2; t++)
            {
                Teams[t].ExtractorCount = 0;
                Teams[t].ForwardPostBonus = 0f;
                Teams[t].ResupplyBonus = 0f;
            }

            // Stealth detection (close enemies or recon units nearby).
            foreach (var u in UnitList)
            {
                if (!u.Alive || u.Def.Ability != AbilityType.Stealth || u.Revealed) continue;
                _abilityList.Clear();
                Spatial.Query(u.Pos, 260f, _abilityList);
                foreach (var o in _abilityList)
                {
                    if (o.Team == u.Team || o.Team == Team.Neutral || !o.Alive) continue;
                    float dist = Vec2.Distance(o.Pos, u.Pos);
                    bool recon = (o.Def.Ability == AbilityType.Recon || o.Def.Ability == AbilityType.Flare) && !o.Constructing;
                    if (dist <= u.Def.AbP1 || (recon && dist <= o.Def.AbRadius)) { u.Revealed = true; break; }
                }
            }

            foreach (var s in UnitList)
            {
                if (!s.Alive || s.DropTimer > 0f) continue;
                var d = s.Def;
                bool active = !s.Constructing;

                // Spotting (needed by indirect-fire units).
                float sight = d.Sight;
                if (active && (d.Ability == AbilityType.Recon || d.Ability == AbilityType.Flare)) sight = Math.Max(sight, d.AbRadius);
                if (sight > 0f)
                {
                    _abilityList.Clear();
                    Spatial.Query(s.Pos, sight, _abilityList);
                    foreach (var o in _abilityList)
                        if (o.Team != s.Team && o.Revealed) o.SpottedBy[(int)s.Team] = true;
                }
                if (!active) continue;

                switch (d.Ability)
                {
                    case AbilityType.Aura:
                    case AbilityType.Fortify:
                    case AbilityType.Flare:
                    case AbilityType.Resupply:
                    {
                        _abilityList.Clear();
                        Spatial.Query(s.Pos, d.AbRadius, _abilityList);
                        foreach (var o in _abilityList)
                        {
                            if (o.Team != s.Team) continue;
                            if (d.Ability == AbilityType.Aura)
                            {
                                o.AuraAttackBonus = Math.Max(o.AuraAttackBonus, d.AbP1);
                                o.AuraDefense = Math.Max(o.AuraDefense, d.AbP2);
                            }
                            else if (d.Ability == AbilityType.Fortify)
                            {
                                if (o.IsInfantry) o.AuraDefense = Math.Max(o.AuraDefense, d.AbP1);
                            }
                            else if (d.Ability == AbilityType.Flare)
                            {
                                o.FlareBonus = Math.Max(o.FlareBonus, d.AbP1);
                            }
                            else if (d.Ability == AbilityType.Resupply && o != s && !o.IsStructure && o.Hp < o.MaxHp)
                            {
                                o.Hp = Math.Min(o.MaxHp, o.Hp + d.AbP2 * 0.5f);
                            }
                        }
                        if (d.Ability == AbilityType.Resupply) Teams[(int)s.Team].ResupplyBonus = Math.Max(Teams[(int)s.Team].ResupplyBonus, d.AbP1);
                        break;
                    }
                    case AbilityType.Fear:
                    case AbilityType.SlowField:
                    {
                        _abilityList.Clear();
                        Spatial.Query(s.Pos, d.AbRadius, _abilityList);
                        foreach (var o in _abilityList)
                        {
                            if (o.Team == s.Team || o.Team == Team.Neutral) continue;
                            if (d.Ability == AbilityType.Fear)
                            {
                                o.FearAttackMult = Math.Min(o.FearAttackMult, 1f - d.AbP1);
                                o.FearSpeedMult = Math.Min(o.FearSpeedMult, 1f - d.AbP2);
                            }
                            else if (o.IsInfantry || o.Def.Armor == ArmorClass.Light)
                            {
                                if (!o.IsAir) o.SlowMult = Math.Min(o.SlowMult, d.AbP1);
                            }
                        }
                        break;
                    }
                    case AbilityType.CpBoost:
                        Teams[(int)s.Team].ExtractorCount++;
                        break;
                    case AbilityType.ForwardPost:
                        Teams[(int)s.Team].ForwardPostBonus += d.AbP1;
                        break;
                }
            }

            // Mines become visible to enemies near recon units.
            foreach (var m in Mines)
            {
                if (!m.Alive || m.Revealed) continue;
                _abilityList.Clear();
                Spatial.Query(m.Pos, 70f, _abilityList);
                foreach (var o in _abilityList)
                {
                    if (o.Team == m.Team) continue;
                    if ((o.Def.Ability == AbilityType.Recon || o.Def.Ability == AbilityType.MineSweep) && Vec2.Distance(o.Pos, m.Pos) <= Math.Max(o.Def.AbRadius, 15f))
                    { m.Revealed = true; break; }
                }
            }
        }
    }
}
