using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed partial class BattleSimulation
    {
        private const int PathBudgetPerTick = 45000;

        // ------------------------------------------------------------------ per-unit update

        private void UpdateUnit(SimUnit u, float dt)
        {
            if (u.DropTimer > 0f)
            {
                u.DropTimer -= dt;
                u.Activity = UnitActivity.Dropping;
                return;
            }

            // timers
            if (u.AttackCooldown > 0f) u.AttackCooldown -= dt;
            if (u.AbilityCooldown > 0f) u.AbilityCooldown -= dt;
            if (u.ChargeTimer > 0f) u.ChargeTimer -= dt;
            if (u.SuppressTimer > 0f) { u.SuppressTimer -= dt; if (u.SuppressTimer <= 0f) u.SuppressMult = 1f; }
            if (u.ActivityTimer > 0f) u.ActivityTimer -= dt;
            if (u.ReloadTimer > 0f)
            {
                u.ReloadTimer -= dt;
                if (u.ReloadTimer <= 0f) u.Ammo = u.Def.Magazine;
            }
            if (u.BurnTimer > 0f)
            {
                u.BurnTimer -= dt;
                ApplyDamage(u, u.BurnDps * dt, DamageType.Flame, null, TeamUtil.Opponent(u.Team), false);
                if (!u.Alive) return;
            }
            if (u.Lifetime > 0f)
            {
                u.Lifetime -= dt;
                if (u.Lifetime <= 0f)
                {
                    if (u.Def.Ability == AbilityType.Suicide) { Explode(u); return; }
                    Kill(u, null, true); // flies off the battlefield
                    return;
                }
            }

            UpdateAbility(u, dt);
            if (!u.Alive) return;

            if (u.IsStructure)
            {
                UpdateStructure(u, dt);
                return;
            }

            if (u.Def.Ability == AbilityType.Suicide && u.AbilityMode() != AbilityMode.Passive)
            {
                UpdateSuicide(u, dt);
                return;
            }

            UpdateOrders(u, dt);
            UpdateActivity(u);
        }

        private void UpdateActivity(SimUnit u)
        {
            if (!u.Alive) return;
            if (u.ActivityTimer > 0f && (u.Activity == UnitActivity.Firing || u.Activity == UnitActivity.Ability)) return;
            float speed = u.Velocity.Length;
            if (u.ReloadTimer > 0f) u.Activity = UnitActivity.Reloading;
            else if (u.TargetId != 0 && speed < 0.3f && u.Def.CanAttack) u.Activity = UnitActivity.Aiming;
            else if (speed > 0.25f) u.Activity = UnitActivity.Moving;
            else u.Activity = UnitActivity.Idle;
        }

        private void UpdateStructure(SimUnit u, float dt)
        {
            if (u.Constructing)
            {
                float rate = u.Def.BuildTime > 0f ? dt / u.Def.BuildTime : 1f;
                u.BuildProgress = Math.Min(1f, u.BuildProgress + rate);
                u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * 0.9f * rate);
                u.Activity = UnitActivity.Constructing;
                if (u.BuildProgress >= 1f)
                {
                    u.Constructing = false;
                    Teams[(int)u.Team].Stats.StructuresBuilt++;
                    Events.Add(new SimEvent { Type = SimEventType.BuildCompleted, Team = u.Team, UnitId = u.Id, DefId = u.Def.Id, Pos = u.Pos });
                }
                return;
            }
            if (!u.Def.CanAttack) { u.Activity = u.Def.Ability != AbilityType.None ? UnitActivity.Ability : UnitActivity.Idle; return; }

            var target = Get(u.TargetId);
            if (!IsValidTarget(u, target, u.Def.Range))
            {
                u.TargetId = 0;
                u.RetargetTimer -= Dt;
                if (u.RetargetTimer <= 0f)
                {
                    u.RetargetTimer = 0.35f;
                    target = AcquireTarget(u, u.Def.Range);
                    u.TargetId = target?.Id ?? 0;
                }
            }
            if (target != null && target.Alive)
            {
                AttackTarget(u, target, dt, true);
            }
            else
            {
                u.Activity = UnitActivity.Idle;
            }
        }

        // ------------------------------------------------------------------ orders

        private void UpdateOrders(SimUnit u, float dt)
        {
            switch (u.Order)
            {
                case OrderType.Move:
                {
                    // Plain move: ignore enemies until arrival (player intent beats auto-AI).
                    float arrive = u.IsAir ? 6f : Math.Max(1.5f, u.Radius + 1f);
                    if (MoveTo(u, u.OrderPos, dt, arrive))
                    {
                        u.Order = OrderType.Idle;
                        u.Anchor = u.Pos;
                        u.Path = null;
                        OnArrived(u);
                    }
                    // Turreted vehicles may still shoot while moving.
                    if (u.HasTurret && u.Def.CanAttack) OpportunityFire(u, dt);
                    else u.TargetId = 0;
                    return;
                }
                case OrderType.AttackUnit:
                {
                    var t = Get(u.OrderTargetId);
                    if (t == null || !t.Alive || !CanTarget(u, t, true))
                    {
                        u.Order = OrderType.Idle;
                        u.Anchor = u.Pos;
                        u.TargetId = 0;
                        return;
                    }
                    u.TargetId = t.Id;
                    if (InRange(u, t)) { StopMoving(u); AttackTarget(u, t, dt, false); }
                    else MoveTo(u, t.Pos, dt, 1f, true);
                    return;
                }
                case OrderType.ClearRubble:
                {
                    if (u.OrderPadId < 0 || u.OrderPadId >= Pads.Length || Pads[u.OrderPadId].State != PadState.Rubble)
                    {
                        u.Order = OrderType.Idle;
                        u.Anchor = u.Pos;
                        return;
                    }
                    float reach = Math.Max(6f, u.Def.AbRadius - 2f);
                    if (Vec2.Distance(u.Pos, Pads[u.OrderPadId].Pos) > reach) MoveTo(u, Pads[u.OrderPadId].Pos, dt, reach);
                    else StopMoving(u);
                    return;
                }
            }

            // Engagement logic shared by Idle / AttackMove / Lane / Hold.
            bool hold = u.Order == OrderType.Hold;
            float acquireRadius = hold || u.Def.Has(UnitFlags.Indirect) ? u.Def.Range : Math.Max(u.Def.Sight, u.Def.Range);
            // Holding a contested zone: hunt enemies anywhere inside it so the fight resolves.
            if (u.Order == OrderType.Lane && u.HoldZoneIndex >= 0 && Zones[u.HoldZoneIndex].Contested && !u.Def.Has(UnitFlags.Indirect))
                acquireRadius = Math.Max(acquireRadius, Zones[u.HoldZoneIndex].Def.Radius * 1.7f);
            SimUnit target = null;
            if (u.Def.CanAttack)
            {
                target = Get(u.TargetId);
                if (!IsValidTarget(u, target, acquireRadius * 1.25f)) { target = null; u.TargetId = 0; }
                u.RetargetTimer -= dt;
                if (u.RetargetTimer <= 0f)
                {
                    u.RetargetTimer = 0.3f + (u.Id % 5) * 0.02f;
                    var better = AcquireTarget(u, acquireRadius);
                    if (better != null && (target == null || (!InRange(u, target) && InRange(u, better)))) target = better;
                    u.TargetId = target?.Id ?? 0;
                }
            }

            if (target != null)
            {
                bool leashOk = true;
                if (u.Order == OrderType.Idle && !u.IsAir) leashOk = Vec2.Distance(target.Pos, u.Anchor) < u.Def.Sight + 25f;
                if (u.Order == OrderType.Lane && u.LaneIndex >= 0) leashOk = Map.Lanes[u.LaneIndex].DistanceTo(target.Pos) < 75f;
                if (InRange(u, target))
                {
                    StopMoving(u);
                    AttackTarget(u, target, dt, false);
                    return;
                }
                if (!hold && leashOk)
                {
                    MoveTo(u, target.Pos, dt, 1f, true);
                    return;
                }
                u.TargetId = 0;
            }

            switch (u.Order)
            {
                case OrderType.AttackMove:
                    if (MoveTo(u, u.OrderPos, dt, u.IsAir ? 6f : 2f))
                    {
                        u.Order = OrderType.Idle;
                        u.Anchor = u.Pos;
                        u.Path = null;
                        OnArrived(u);
                    }
                    break;
                case OrderType.Lane:
                    UpdateLane(u, dt);
                    break;
                case OrderType.Idle:
                    if (u.IsAir && u.Def.Has(UnitFlags.Loiter)) Loiter(u, dt);
                    else if (Vec2.Distance(u.Pos, u.Anchor) > 4f) MoveTo(u, u.Anchor, dt, 2f);
                    else StopMoving(u);
                    break;
                default:
                    StopMoving(u);
                    break;
            }
        }

        private void OnArrived(SimUnit u)
        {
            // Transports drop their troops on arrival.
            if (u.Def.Ability == AbilityType.Transport && u.PassengerCount > 0) Dismount(u, 1f);
        }

        private void OpportunityFire(SimUnit u, float dt)
        {
            var t = Get(u.TargetId);
            u.RetargetTimer -= dt;
            if (!IsValidTarget(u, t, u.Def.Range) || u.RetargetTimer <= 0f)
            {
                u.RetargetTimer = 0.4f;
                t = AcquireTarget(u, u.Def.Range);
                u.TargetId = t?.Id ?? 0;
            }
            if (t != null && InRange(u, t)) AttackTarget(u, t, dt, true);
        }

        private void Loiter(SimUnit u, float dt)
        {
            // circle the anchor
            float ang = (Time * 0.35f + u.Id) % MathUtil.TwoPi;
            float r = 22f + (u.Id % 3) * 6f;
            Vec2 goal = u.Anchor + Vec2.FromYaw(ang) * r;
            MoveTo(u, goal, dt, 2f);
        }

        // ------------------------------------------------------------------ lane AI

        public void AssignLane(SimUnit u, int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= Map.Lanes.Count) return;
            u.Order = OrderType.Lane;
            u.LaneIndex = laneIndex;
            u.LaneDir = u.Team == Team.Player ? 1 : -1;
            u.LaneProgress = Map.Lanes[laneIndex].NearestIndex(u.Pos);
            u.HoldZoneIndex = -1;
            u.Path = null;
            u.PlayerControlled = false;
        }

        private void UpdateLane(SimUnit u, float dt)
        {
            var lane = Map.Lanes[u.LaneIndex];

            // Holding a zone until it is ours.
            if (u.HoldZoneIndex >= 0)
            {
                var z = Zones[u.HoldZoneIndex];
                if (z.Owner == u.Team && Math.Abs(z.Control) >= 0.999f) u.HoldZoneIndex = -1;
                else
                {
                    if (Vec2.Distance(u.Pos, u.HoldPoint) > 3f) MoveTo(u, u.HoldPoint, dt, 2.5f);
                    else StopMoving(u);
                    return;
                }
            }

            // Next checkpoint zone in our direction that we don't own => go capture it.
            foreach (var cp in lane.Checkpoints)
            {
                bool ahead = u.LaneDir > 0 ? cp.PathIndex >= u.LaneProgress - 2 : cp.PathIndex <= u.LaneProgress + 2;
                if (!ahead) continue;
                bool reached = u.LaneDir > 0 ? u.LaneProgress >= cp.PathIndex - 3 : u.LaneProgress <= cp.PathIndex + 3;
                if (!reached) break;
                var zone = ZoneById(cp.ZoneId);
                if (zone != null && (zone.Owner != u.Team || Math.Abs(zone.Control) < 0.999f))
                {
                    u.HoldZoneIndex = zone.Index;
                    u.HoldPoint = zone.Def.Center + Rng.InsideCircle(zone.Def.Radius * 0.4f);
                    Map.Nav.NearestWalkable(u.HoldPoint, u.Amphibious, 6, out u.HoldPoint);
                    return;
                }
                break;
            }

            int lastIdx = lane.Count - 1;
            Vec2 wp = lane[u.LaneProgress];
            if (Vec2.Distance(u.Pos, wp) < 9f)
            {
                int next = u.LaneProgress + u.LaneDir * 2;
                u.LaneProgress = MathUtil.Clamp(next, 0, lastIdx);
                wp = lane[u.LaneProgress];
            }
            bool atEnd = (u.LaneDir > 0 && u.LaneProgress >= lastIdx) || (u.LaneDir < 0 && u.LaneProgress <= 0);
            if (atEnd)
            {
                // At the enemy HQ: attack it.
                var hq = Hq(TeamUtil.Opponent(u.Team));
                if (hq != null && hq.Alive)
                {
                    u.TargetId = hq.Id;
                    if (InRange(u, hq)) { StopMoving(u); AttackTarget(u, hq, dt, false); }
                    else MoveTo(u, hq.Pos, dt, 1f, true);
                }
                return;
            }
            MoveTo(u, wp, dt, 3f);
        }

        // ------------------------------------------------------------------ movement

        private void StopMoving(SimUnit u)
        {
            u.Velocity = Vec2.Zero;
        }

        private float MoveSpeed(SimUnit u)
        {
            float s = u.Def.MoveSpeed;
            if (s <= 0f) return 0f;
            if (!u.IsAir)
            {
                float cost = Map.Nav.CostAt(u.Pos);
                s *= MathUtil.Clamp(1.15f / cost, 0.6f, 1.15f);
            }
            if (u.ChargeTimer > 0f) s *= Math.Max(1f, u.Def.AbP1);
            if (u.SuppressTimer > 0f) s *= u.SuppressMult;
            s *= u.SlowMult * u.FearSpeedMult;
            return s;
        }

        /// <summary>Moves toward goal following a path. Returns true when within arriveRadius.</summary>
        private bool MoveTo(SimUnit u, Vec2 goal, float dt, float arriveRadius, bool dynamicGoal = false)
        {
            float dist = Vec2.Distance(u.Pos, goal);
            if (dist <= arriveRadius) { StopMoving(u); return true; }

            Vec2 steerTarget = goal;
            if (!u.IsAir)
            {
                bool needPath = u.Path == null || Vec2.Distance(u.PathGoal, goal) > (dynamicGoal ? 6f : 1.5f);
                if (dynamicGoal && u.Path != null)
                {
                    u.RepathTimer -= dt;
                    if (u.RepathTimer <= 0f && Vec2.Distance(u.PathGoal, goal) > 3f) needPath = true;
                }
                if (needPath && !u.PathPending)
                {
                    if (Map.Nav.LineWalkable(u.Pos, goal, u.Amphibious))
                    {
                        u.Path = new List<Vec2>(1) { goal };
                        u.PathIndex = 0;
                        u.PathGoal = goal;
                    }
                    else RequestPath(u, goal);
                    u.RepathTimer = 1f;
                }
                if (u.Path != null && u.PathIndex < u.Path.Count)
                {
                    steerTarget = u.Path[u.PathIndex];
                    float reach = u.IsVehicle ? 3f : 2f;
                    while (u.PathIndex < u.Path.Count - 1 && Vec2.Distance(u.Pos, u.Path[u.PathIndex]) < reach)
                    {
                        u.PathIndex++;
                        steerTarget = u.Path[u.PathIndex];
                    }
                }
                else if (u.PathPending)
                {
                    // Waiting for a path: only move if the straight line is clear.
                    if (!Map.Nav.LineWalkable(u.Pos, goal, u.Amphibious)) { StopMoving(u); return false; }
                }
            }

            Vec2 desired = (steerTarget - u.Pos).Normalized;
            float speed = MoveSpeed(u);
            float targetYaw = desired.ToYaw();

            // Turning: tracked vehicles must face their direction before driving fast.
            float turnRate = u.IsInfantry ? 9f : (u.IsAir ? 2.2f : (u.Def.Mobility == Mobility.Tracked ? 1.6f : 2.4f));
            u.Yaw = MathUtil.MoveTowardsAngle(u.Yaw, targetYaw, turnRate * dt);
            if (u.IsVehicle)
            {
                float d = Math.Abs(MathUtil.DeltaAngle(u.Yaw, targetYaw));
                speed *= MathUtil.Clamp(MathF.Cos(d), 0.15f, 1f);
            }

            Vec2 vel = desired * speed;

            // Separation from nearby ground units.
            if (!u.IsAir)
            {
                _scratchA.Clear();
                Spatial.Query(u.Pos, u.Radius + 3.2f, _scratchA);
                Vec2 push = Vec2.Zero;
                foreach (var o in _scratchA)
                {
                    if (o == u || o.IsAir || !o.Alive) continue;
                    float minD = u.Radius + o.Radius + 0.4f;
                    Vec2 diff = u.Pos - o.Pos;
                    float d = diff.Length;
                    if (d < minD && d > 1e-3f) push += diff / d * ((minD - d) / minD);
                    else if (d <= 1e-3f) push += new Vec2((u.Id % 3) - 1, ((u.Id / 3) % 3) - 1) * 0.5f;
                }
                vel += push * Math.Max(2.5f, speed * 0.9f);
            }

            Vec2 next = u.Pos + vel * dt;
            if (!u.IsAir)
            {
                if (!Map.Nav.IsWalkable(next, u.Amphibious))
                {
                    Vec2 nx = new Vec2(next.X, u.Pos.Z), nz = new Vec2(u.Pos.X, next.Z);
                    if (Map.Nav.IsWalkable(nx, u.Amphibious)) next = nx;
                    else if (Map.Nav.IsWalkable(nz, u.Amphibious)) next = nz;
                    else next = u.Pos;
                    u.BlockedTicks++;
                    if (u.BlockedTicks > 20 && !u.PathPending) { u.BlockedTicks = 0; RequestPath(u, goal); }
                }
                else u.BlockedTicks = 0;
            }
            else next = Map.ClampToMap(next, 4f);

            u.Velocity = (next - u.Pos) / dt;
            u.Pos = next;
            return Vec2.Distance(u.Pos, goal) <= arriveRadius;
        }

        private void RequestPath(SimUnit u, Vec2 goal)
        {
            u.PathPending = true;
            u.PathGoal = goal;
            _pathQueue.Enqueue(u.Id);
        }

        /// <summary>Bounded A* work per tick (path recomputation rate limiting).</summary>
        private void ProcessPathQueue()
        {
            int budget = PathBudgetPerTick;
            while (budget > 0 && _pathQueue.Count > 0)
            {
                var u = Get(_pathQueue.Dequeue());
                if (u == null || !u.Alive || !u.PathPending) continue;
                var path = _pathfinder.FindPath(u.Pos, u.PathGoal, u.Amphibious);
                PathsComputedTotal++;
                budget -= Math.Max(400, _pathfinder.LastExpanded);
                u.PathPending = false;
                u.Path = path ?? new List<Vec2>(1) { u.PathGoal };
                u.PathIndex = 0;
            }
        }

        // ------------------------------------------------------------------ targeting

        public bool CanTarget(SimUnit attacker, SimUnit t, bool explicitOrder)
        {
            if (t == null || !t.Targetable || t.Team == attacker.Team || t.Team == Team.Neutral) return false;
            if (t.IsAir ? !attacker.Def.HitAir : !attacker.Def.HitGround) return false;
            if (t.Def.Ability == AbilityType.Stealth && !t.Revealed) return false;
            if (attacker.Def.Has(UnitFlags.Indirect) && !t.SpottedBy[(int)attacker.Team] && !explicitOrder) return false;
            if (Config.DamageMultiplier(attacker.Def.DamageType, t.Def.Armor) <= 0f) return false;
            return true;
        }

        private bool IsValidTarget(SimUnit u, SimUnit t, float maxDist)
        {
            if (t == null || !CanTarget(u, t, false)) return false;
            return Vec2.Distance(u.Pos, t.Pos) <= maxDist + t.Radius;
        }

        public bool InRange(SimUnit u, SimUnit t)
        {
            float r = u.Def.Range + t.Radius + u.Radius * 0.5f;
            return Vec2.DistanceSq(u.Pos, t.Pos) <= r * r;
        }

        private SimUnit AcquireTarget(SimUnit u, float radius)
        {
            if (!u.Def.CanAttack) return null;
            _scratchB.Clear();
            Spatial.Query(u.Pos, radius + 4f, _scratchB);
            SimUnit best = null;
            float bestScore = float.MaxValue;
            foreach (var t in _scratchB)
            {
                if (!CanTarget(u, t, false)) continue;
                float d = Vec2.Distance(u.Pos, t.Pos) - t.Radius;
                if (d > radius) continue;
                float mult = Config.DamageMultiplier(u.Def.DamageType, t.Def.Armor);
                float score = d / (0.35f + mult);
                if (t.IsStructure) score += t.Def.IsHQ ? 25f : 10f;
                if (t.Constructing) score += 5f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            return best;
        }
    }

    internal static class SimUnitExt
    {
        public static AbilityMode AbilityMode(this SimUnit u) => u.Def.AbilityMode;
    }
}
