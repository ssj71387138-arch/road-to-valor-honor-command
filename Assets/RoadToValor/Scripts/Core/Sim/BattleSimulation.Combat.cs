using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed partial class BattleSimulation
    {
        private readonly List<SimUnit> _splashList = new List<SimUnit>(64);

        private float AttackMultiplier(SimUnit u)
        {
            float m = Teams[(int)u.Team].AttackMultiplier;
            m *= 1f + u.AuraAttackBonus + u.FlareBonus;
            m *= u.FearAttackMult;
            return m;
        }

        /// <summary>Face and shoot. Handles magazine, reload, projectiles and on-hit abilities.</summary>
        private void AttackTarget(SimUnit u, SimUnit t, float dt, bool turretOnly)
        {
            Vec2 to = t.Pos - u.Pos;
            float aimYaw = to.ToYaw();
            u.AimPoint = t.Pos;
            bool aligned;
            if (u.HasTurret)
            {
                u.TurretYaw = MathUtil.MoveTowardsAngle(u.TurretYaw, aimYaw, (u.IsStructure ? 2.5f : 1.8f) * dt);
                aligned = Math.Abs(MathUtil.DeltaAngle(u.TurretYaw, aimYaw)) < 0.15f;
                if (!turretOnly && u.IsVehicle && u.Def.Mobility != Mobility.Tracked)
                    u.Yaw = MathUtil.MoveTowardsAngle(u.Yaw, aimYaw, 2f * dt);
            }
            else
            {
                float rate = u.IsInfantry ? 9f : (u.IsAir ? 3f : 2.2f);
                u.Yaw = MathUtil.MoveTowardsAngle(u.Yaw, aimYaw, rate * dt);
                u.TurretYaw = u.Yaw;
                aligned = Math.Abs(MathUtil.DeltaAngle(u.Yaw, aimYaw)) < (u.IsAir ? 0.6f : 0.35f);
            }

            if (u.ReloadTimer > 0f) { u.Activity = UnitActivity.Reloading; return; }
            if (u.AttackCooldown > 0f || !aligned)
            {
                if (u.ActivityTimer <= 0f) u.Activity = UnitActivity.Aiming;
                return;
            }

            Fire(u, t);
        }

        private void Fire(SimUnit u, SimUnit t)
        {
            var def = u.Def;
            float dmg = def.Attack * AttackMultiplier(u);
            u.LastFireTime = Time;
            u.FireFlashTime = Time;
            u.Activity = UnitActivity.Firing;
            u.ActivityTimer = 0.18f;

            if (def.ProjectileSpeed > 0f && Projectiles.Count < Config.MaxProjectiles)
            {
                Vec2 aim = t.Pos;
                if (def.Has(UnitFlags.Indirect)) aim += Rng.InsideCircle(Math.Max(1.5f, def.Splash * 0.45f));
                float dist = Vec2.Distance(u.Pos, aim);
                var p = new Projectile
                {
                    Id = _nextProjectileId++,
                    Team = u.Team,
                    SourceId = u.Id,
                    SourceDefId = def.Id,
                    TargetId = t.Id,
                    From = u.Pos,
                    To = aim,
                    Duration = Math.Max(0.08f, dist / def.ProjectileSpeed),
                    Damage = dmg,
                    Type = def.DamageType,
                    Splash = def.Splash,
                    Indirect = def.Has(UnitFlags.Indirect),
                    TargetAir = t.IsAir,
                    Alive = true,
                };
                Projectiles.Add(p);
                Events.Add(new SimEvent { Type = SimEventType.ProjectileLaunched, Team = u.Team, UnitId = u.Id, OtherId = p.Id, DefId = def.Id, Pos = u.Pos, Pos2 = aim, Value = p.Duration, Value2 = p.Indirect ? 1f : 0f, Damage = def.DamageType });
            }
            else
            {
                Events.Add(new SimEvent { Type = SimEventType.Shot, Team = u.Team, UnitId = u.Id, OtherId = t.Id, DefId = def.Id, Pos = u.Pos, Pos2 = t.Pos, Damage = def.DamageType });
                if (def.Splash > 0f) SplashDamage(t.Pos, def.Splash, dmg, def.DamageType, u, u.Team, t.IsAir);
                else ApplyDamage(t, dmg, def.DamageType, u, u.Team, true);

                if (def.Ability == AbilityType.Tesla) TeslaChain(u, t, dmg);
            }

            if (def.Magazine > 0)
            {
                u.Ammo--;
                if (u.Ammo <= 0) { u.ReloadTimer = Math.Max(0.1f, def.ReloadTime); u.Activity = UnitActivity.Reloading; }
            }
            float interval = def.AttackInterval;
            if (u.ChargeTimer > 0f) interval *= 0.75f;
            if (u.SuppressTimer > 0f) interval *= 1.3f;
            u.AttackCooldown = interval;
        }

        private void TeslaChain(SimUnit src, SimUnit first, float dmg)
        {
            int chains = (int)src.Def.AbP1;
            float falloff = src.Def.AbP2 > 0f ? src.Def.AbP2 : 0.7f;
            float jump = src.Def.AbRadius > 0f ? src.Def.AbRadius : 12f;
            var hit = new HashSet<int> { first.Id };
            SimUnit cur = first;
            for (int i = 0; i < chains; i++)
            {
                dmg *= falloff;
                _splashList.Clear();
                Spatial.Query(cur.Pos, jump, _splashList);
                SimUnit next = null;
                float bd = float.MaxValue;
                foreach (var o in _splashList)
                {
                    if (hit.Contains(o.Id) || !CanTarget(src, o, false)) continue;
                    float d = Vec2.DistanceSq(o.Pos, cur.Pos);
                    if (d < bd) { bd = d; next = o; }
                }
                if (next == null) break;
                hit.Add(next.Id);
                Events.Add(new SimEvent { Type = SimEventType.TeslaArc, Team = src.Team, UnitId = cur.Id, OtherId = next.Id, Pos = cur.Pos, Pos2 = next.Pos });
                ApplyDamage(next, dmg, DamageType.Tesla, src, src.Team, true);
                cur = next;
            }
        }

        /// <summary>Central damage function (armour matrix, shields, auras, on-hit effects, kill credit).</summary>
        public void ApplyDamage(SimUnit t, float amount, DamageType type, SimUnit attacker, Team attackerTeam, bool onHitEffects)
        {
            if (t == null || !t.Alive || amount <= 0f) return;
            float mult = Config.DamageMultiplier(type, t.Def.Armor);
            if (mult <= 0f) return;
            float reduction = t.AuraDefense;
            if (t.Def.Ability == AbilityType.Shield && (type == DamageType.Bullet || type == DamageType.Sniper || type == DamageType.Melee))
                reduction = Math.Max(reduction, t.Def.AbP1);
            reduction = MathUtil.Clamp(reduction, 0f, 0.85f);
            float final = amount * mult * (1f - reduction);
            t.Hp -= final;
            t.LastHitTime = Time;
            if (attacker != null)
            {
                attacker.DamageDealt += final;
                Teams[(int)attacker.Team].Stats.DamageDealt += final;
            }

            if (onHitEffects && attacker != null && t.Alive)
            {
                var ad = attacker.Def;
                if (ad.Ability == AbilityType.Suppress && t.IsInfantry)
                {
                    t.SuppressTimer = Math.Max(t.SuppressTimer, ad.AbP2);
                    t.SuppressMult = Math.Min(t.SuppressMult, ad.AbP1 > 0f ? ad.AbP1 : 0.6f);
                }
                if (ad.Ability == AbilityType.Burn && !t.IsAir)
                {
                    t.BurnTimer = Math.Max(t.BurnTimer, ad.AbP2);
                    t.BurnDps = Math.Max(t.BurnDps, ad.AbP1);
                }
            }

            if (t.Hp <= 0f) Kill(t, attacker);
        }

        private void SplashDamage(Vec2 center, float radius, float damage, DamageType type, SimUnit attacker, Team team, bool includeAir)
        {
            _splashList.Clear();
            Spatial.Query(center, radius + 3.5f, _splashList);
            foreach (var o in _splashList)
            {
                if (o.Team == team || o.Team == Team.Neutral || !o.Targetable) continue;
                if (o.IsAir && !includeAir) continue;
                float d = Math.Max(0f, Vec2.Distance(o.Pos, center) - o.Radius);
                if (d > radius) continue;
                float falloff = 1f - 0.5f * (d / Math.Max(0.1f, radius));
                ApplyDamage(o, damage * falloff, type, attacker, team, true);
            }
        }

        private void UpdateProjectiles(float dt)
        {
            for (int i = Projectiles.Count - 1; i >= 0; i--)
            {
                var p = Projectiles[i];
                p.Elapsed += dt;
                if (p.Elapsed < p.Duration) continue;
                p.Alive = false;
                Projectiles.RemoveAt(i);
                var src = Get(p.SourceId);
                if (p.Splash > 0f)
                {
                    SplashDamage(p.To, p.Splash, p.Damage, p.Type, src, p.Team, p.TargetAir);
                }
                else
                {
                    // Direct shot: hits if the target is still near the aim point.
                    var t = Get(p.TargetId);
                    if (t != null && t.Alive && Vec2.Distance(t.Pos, p.To) < t.Radius + 3.5f)
                        ApplyDamage(t, p.Damage, p.Type, src, p.Team, true);
                }
                Events.Add(new SimEvent { Type = SimEventType.Impact, Team = p.Team, DefId = p.SourceDefId, Pos = p.To, Value = Math.Max(1.5f, p.Splash), Damage = p.Type, Value2 = p.TargetAir ? 1f : 0f });
            }
        }

        public void ScheduleStrike(Team team, Vec2 pos, float delay, float radius, float damage, DamageType type, int sourceDefId, bool big = false)
        {
            Strikes.Add(new Strike { Team = team, Time = Time + delay, Pos = pos, Radius = radius, Damage = damage, Type = type, SourceDefId = sourceDefId, Big = big });
        }

        private void UpdateStrikes()
        {
            for (int i = Strikes.Count - 1; i >= 0; i--)
            {
                var s = Strikes[i];
                if (Time < s.Time) continue;
                Strikes.RemoveAt(i);
                SplashDamage(s.Pos, s.Radius, s.Damage, s.Type, null, s.Team, false);
                Events.Add(new SimEvent { Type = SimEventType.Explosion, Team = s.Team, DefId = s.SourceDefId, Pos = s.Pos, Value = s.Radius, Value2 = s.Big ? 2f : 1f });
            }
        }

        private float _mineTimer;

        private void UpdateMines()
        {
            _mineTimer -= Dt;
            if (_mineTimer > 0f) return;
            _mineTimer = 0.2f;
            for (int i = Mines.Count - 1; i >= 0; i--)
            {
                var m = Mines[i];
                if (!m.Alive) { Mines.RemoveAt(i); continue; }
                _splashList.Clear();
                Spatial.Query(m.Pos, m.TriggerRadius + 3f, _splashList);
                SimUnit victim = null;
                foreach (var o in _splashList)
                {
                    if (o.Team == m.Team || o.Team == Team.Neutral || !o.Targetable || !o.IsGround) continue;
                    if (m.AntiTankOnly && !o.IsVehicle) continue;
                    if (Vec2.Distance(o.Pos, m.Pos) <= m.TriggerRadius + o.Radius) { victim = o; break; }
                }
                if (victim == null) continue;
                m.Alive = false;
                Mines.RemoveAt(i);
                var owner = Get(m.OwnerId);
                SplashDamage(m.Pos, m.AntiTankOnly ? 3.5f : 5f, m.Damage, m.AntiTankOnly ? DamageType.AP : DamageType.HE, owner, m.Team, false);
                Events.Add(new SimEvent { Type = SimEventType.MineTriggered, Team = m.Team, UnitId = m.Id, Pos = m.Pos, Value = 4f });
            }
        }

        // ------------------------------------------------------------------ suicide units

        private void UpdateSuicide(SimUnit u, float dt)
        {
            var t = Get(u.TargetId);
            float sight = Math.Max(u.Def.Sight, 25f);
            if (t == null || !t.Targetable || t.Team == u.Team || (t.IsAir))
            {
                t = null;
                _scratchB.Clear();
                Spatial.Query(u.Pos, sight, _scratchB);
                float bd = float.MaxValue;
                foreach (var o in _scratchB)
                {
                    if (o.Team == u.Team || o.Team == Team.Neutral || !o.Targetable || o.IsAir) continue;
                    if (o.Def.Ability == AbilityType.Stealth && !o.Revealed) continue;
                    float d = Vec2.DistanceSq(o.Pos, u.Pos);
                    if (d < bd) { bd = d; t = o; }
                }
                u.TargetId = t?.Id ?? 0;
            }

            if (t != null && (u.Order != OrderType.Move || u.IsAir))
            {
                if (Vec2.Distance(u.Pos, t.Pos) <= u.Radius + t.Radius + 1.6f) { Explode(u); return; }
                MoveTo(u, t.Pos, dt, 0.5f, true);
                u.Activity = UnitActivity.Moving;
                return;
            }

            switch (u.Order)
            {
                case OrderType.Move:
                case OrderType.AttackMove:
                    if (MoveTo(u, u.OrderPos, dt, 3f)) { u.Order = OrderType.Idle; u.Anchor = u.Pos; }
                    break;
                case OrderType.Lane:
                    UpdateLane(u, dt);
                    break;
                default:
                    if (u.IsAir) Loiter(u, dt);
                    else StopMoving(u);
                    break;
            }
            UpdateActivity(u);
        }

        private void Explode(SimUnit u)
        {
            float r = u.Def.AbRadius > 0f ? u.Def.AbRadius : 6f;
            Events.Add(new SimEvent { Type = SimEventType.Explosion, Team = u.Team, DefId = u.Def.Id, Pos = u.Pos, Value = r, Value2 = 1f });
            Kill(u, null, true);
            u.DeathKind = 2;
            SplashDamage(u.Pos, r, u.Def.AbP1, DamageType.HE, u, u.Team, false);
        }
    }
}
