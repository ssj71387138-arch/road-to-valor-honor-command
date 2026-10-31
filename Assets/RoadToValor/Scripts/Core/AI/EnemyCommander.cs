using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>
    /// Card-playing commander AI. Plays by the same rules as the player (CP, deck, deploy zones, pads):
    /// defends its HQ, picks a lane to push, deploys at its most forward zone on that lane, builds on free pads,
    /// clears rubble with engineers and uses strike skills on clustered enemies.
    /// </summary>
    public sealed class EnemyCommander
    {
        private readonly BattleSimulation _sim;
        public readonly Team Team;
        private readonly DifficultySettings _diff;
        private float _think;
        private int _plannedCard = -1;
        private int _plannedLane = 1;
        private readonly Rng _rng;
        public string LastDecision = "";

        public EnemyCommander(BattleSimulation sim, Team team, DifficultySettings diff)
        {
            _sim = sim;
            Team = team;
            _diff = diff ?? new DifficultySettings();
            _rng = new Rng(sim.Setup.Seed * 31 + (int)team);
            _think = 2f;
        }

        private Team Foe => TeamUtil.Opponent(Team);
        private TeamState Me => _sim.TeamOf(Team);

        public void Update(float dt)
        {
            _think -= dt;
            if (_think > 0f) return;
            _think = _diff.ThinkInterval * _rng.Range(0.8f, 1.2f);
            if (_sim.IsOver) return;
            Think();
        }

        private bool Affordable(UnitDefinition d) => Me.Cp + 1e-4f >= d.CpCost && Me.CardCooldown(d.Id) <= 0f;

        private void Think()
        {
            var deck = Me.Deck;
            var hqDef = _sim.Map.Layout.GetHq(Team);

            // 0) Idle ground units (e.g. engineers after clearing rubble, dismounted troops) rejoin a lane.
            foreach (var u in _sim.UnitList)
            {
                if (!u.Alive || u.Team != Team || u.IsStructure || u.IsAir || u.Order != OrderType.Idle) continue;
                if (u.Def.Ability == AbilityType.Repair && HasRubbleNear(u.Pos)) continue;
                if (_sim.Time - u.SpawnTime < 3f) continue;
                _sim.AssignLane(u, NearestLane(u.Pos));
            }

            // 1) Emergency HQ defence.
            int threat = hqDef != null ? _sim.Spatial.Count(hqDef.Position, 140f, Foe, true) : 0;
            if (threat >= 4)
            {
                var card = BestGroundCard(true);
                if (card != null && Affordable(card))
                {
                    Vec2 p = hqDef.Position + (Toward(hqDef.Position) * 35f);
                    if (Deploy(card, p, -1, out _)) { GiveLaneOrders(NearestLane(p)); LastDecision = "본진 방어: " + card.NameKo; return; }
                }
            }

            // 2) Skills on clustered enemies / supply drop when low.
            foreach (int id in deck)
            {
                var d = _sim.Units.Get(id);
                if (d == null || !d.IsSkill || !Affordable(d)) continue;
                if (d.Ability == AbilityType.SupplyDrop)
                {
                    if (Me.Cp <= 4f && hqDef != null && Deploy(d, hqDef.Position, -1, out _)) { LastDecision = "보급 투하"; return; }
                    continue;
                }
                if (d.Ability == AbilityType.CargoDrop)
                {
                    if (Me.Cp <= 6f && hqDef != null && Deploy(d, hqDef.Position, -1, out _)) { LastDecision = "화물 수송선"; return; }
                    continue;
                }
                if (FindCluster(out Vec2 c, out int n) && n >= _diff.SkillMinTargets)
                {
                    if (Deploy(d, c, -1, out _)) { LastDecision = d.NameKo + " -> 적 " + n + "기"; return; }
                }
            }

            // 3) Clear rubble in our zones with engineers.
            foreach (var pad in _sim.Pads)
            {
                if (pad.State != PadState.Rubble || _sim.Zones[pad.ZoneIndex].Owner != Team) continue;
                bool engineerNear = false;
                foreach (var u in _sim.UnitList)
                    if (u.Alive && u.Team == Team && u.Def.Ability == AbilityType.Repair && Vec2.Distance(u.Pos, pad.Pos) < 60f)
                    {
                        engineerNear = true;
                        if (u.Order != OrderType.ClearRubble) _sim.Enqueue(new GameCommand { Type = CommandType.ClearRubble, Team = Team, UnitIds = new[] { u.Id }, PadId = pad.Id, Target = pad.Pos });
                    }
                if (engineerNear) continue;
                foreach (int id in deck)
                {
                    var d = _sim.Units.Get(id);
                    if (d == null || d.Ability != AbilityType.Repair || d.IsStructure || !Affordable(d)) continue;
                    Vec2 p = pad.Pos + Toward(pad.Pos) * -10f;
                    if (Deploy(d, p, -1, out _))
                    {
                        var ids = new List<int>();
                        foreach (var u in _sim.LastDeployed) ids.Add(u.Id);
                        _sim.Enqueue(new GameCommand { Type = CommandType.ClearRubble, Team = Team, UnitIds = ids.ToArray(), PadId = pad.Id, Target = pad.Pos });
                        LastDecision = "잔해 철거: " + d.NameKo;
                        return;
                    }
                }
                break;
            }

            // 4) Build on free pads of forward zones.
            if (_rng.Chance(0.35f))
            {
                foreach (int id in deck)
                {
                    var d = _sim.Units.Get(id);
                    if (d == null || !d.IsStructure || !Affordable(d)) continue;
                    BuildPad best = null;
                    float bestFront = float.MaxValue;
                    foreach (var pad in _sim.Pads)
                    {
                        var z = _sim.Zones[pad.ZoneIndex];
                        if (z.Owner != Team || pad.State != PadState.Empty) continue;
                        if (_sim.StructuresInZone(pad.ZoneIndex) >= _sim.Config.MaxBuildingsPerZone) continue;
                        float front = DistToFoeHq(z.Def.Center) + _rng.Range(0f, 80f);
                        if (front < bestFront) { bestFront = front; best = pad; }
                    }
                    if (best != null && Deploy(d, best.Pos, best.Id, out _)) { LastDecision = "건설: " + d.NameKo; return; }
                }
            }

            // 5) Push a lane with a planned unit card (save CP until affordable).
            if (_plannedCard < 0 || _sim.Units.Get(_plannedCard) == null)
            {
                var c = PickPushCard();
                _plannedCard = c?.Id ?? -1;
                _plannedLane = ChooseLane();
            }
            var planned = _sim.Units.Get(_plannedCard);
            if (planned == null || !Affordable(planned)) return;

            Vec2 deployAt = DeployPointForLane(_plannedLane, planned);
            if (planned.IsAir) deployAt = FrontPoint(_plannedLane);
            if (Deploy(planned, deployAt, -1, out string reason))
            {
                if (!planned.IsAir) GiveLaneOrders(_plannedLane);
                LastDecision = _sim.Map.Lanes[_plannedLane].Def.Name + " 공격: " + planned.NameKo;
                _plannedCard = -1;
            }
            else
            {
                LastDecision = "배치 실패 (" + reason + ")";
                _plannedCard = -1;
            }
        }

        private bool HasRubbleNear(Vec2 p)
        {
            foreach (var pad in _sim.Pads)
                if (pad.State == PadState.Rubble && _sim.Zones[pad.ZoneIndex].Owner == Team && Vec2.Distance(pad.Pos, p) < 60f) return true;
            return false;
        }

        private bool Deploy(UnitDefinition d, Vec2 p, int pad, out string reason) => _sim.TryDeploy(Team, d.Id, p, pad, out reason);

        private void GiveLaneOrders(int lane)
        {
            foreach (var u in _sim.LastDeployed)
                if (u.Alive && !u.IsStructure && !u.IsAir) _sim.AssignLane(u, lane);
        }

        private Vec2 Toward(Vec2 from)
        {
            var foe = _sim.Map.Layout.GetHq(Foe);
            return foe != null ? (foe.Position - from).Normalized : new Vec2(0, Team == Team.Player ? 1 : -1);
        }

        private float DistToFoeHq(Vec2 p)
        {
            var foe = _sim.Map.Layout.GetHq(Foe);
            return foe != null ? Vec2.Distance(p, foe.Position) : 0f;
        }

        private UnitDefinition BestGroundCard(bool cheap)
        {
            UnitDefinition best = null;
            foreach (int id in Me.Deck)
            {
                var d = _sim.Units.Get(id);
                if (d == null || !d.IsGroundMobile || !d.CanAttack) continue;
                if (best == null || (cheap ? d.CpCost < best.CpCost : d.CpCost > best.CpCost)) best = d;
            }
            return best;
        }

        private UnitDefinition PickPushCard()
        {
            var options = new List<UnitDefinition>();
            float total = 0f;
            foreach (int id in Me.Deck)
            {
                var d = _sim.Units.Get(id);
                if (d == null || d.IsSkill || d.IsStructure) continue;
                if (d.Ability == AbilityType.Repair) continue; // engineers are used for rubble
                options.Add(d);
                total += 1f + d.CpCost * 0.3f;
            }
            if (options.Count == 0) return null;
            float r = _rng.Range(0f, total);
            foreach (var d in options)
            {
                r -= 1f + d.CpCost * 0.3f;
                if (r <= 0f) return d;
            }
            return options[options.Count - 1];
        }

        public int NearestLane(Vec2 p)
        {
            int best = 1;
            float bd = float.MaxValue;
            for (int i = 0; i < _sim.Map.Lanes.Count; i++)
            {
                float d = _sim.Map.Lanes[i].DistanceTo(p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        private int ChooseLane()
        {
            int lanes = _sim.Map.Lanes.Count;
            var score = new float[lanes];
            foreach (var u in _sim.UnitList)
            {
                if (!u.Alive || u.IsStructure || u.IsAir) continue;
                int lane = LaneByX(u.Pos.X);
                if (u.Team == Foe)
                {
                    // Threat on our side of the map counts double.
                    bool ourSide = Team == Team.Enemy ? u.Pos.Z > 0f : u.Pos.Z < 0f;
                    score[lane] += ourSide ? 2f : 1f;
                }
                else if (u.Team == Team) score[lane] -= 0.8f;
            }
            for (int i = 0; i < lanes; i++)
            {
                if (!LaneFrontOwned(i)) score[i] += 2f;
                score[i] += _rng.Range(0f, 3f);
            }
            int best = 0;
            for (int i = 1; i < lanes; i++) if (score[i] > score[best]) best = i;
            return best;
        }

        private static int LaneByX(float x) => x < -150f ? 0 : (x > 150f ? 2 : 1);

        private bool LaneFrontOwned(int lane)
        {
            foreach (var cp in _sim.Map.Lanes[lane].Checkpoints)
            {
                var z = _sim.ZoneById(cp.ZoneId);
                if (z != null && z.Owner != Team) return false;
            }
            return true;
        }

        /// <summary>Most forward zone we own on the lane (deploy point), else HQ gate.</summary>
        private Vec2 DeployPointForLane(int lane, UnitDefinition d)
        {
            var lp = _sim.Map.Lanes[lane];
            ZoneState best = null;
            float bestDist = float.MaxValue;
            foreach (var cp in lp.Checkpoints)
            {
                var z = _sim.ZoneById(cp.ZoneId);
                if (z == null || z.Owner != Team || z.Contested) continue;
                float dd = DistToFoeHq(z.Def.Center);
                if (dd < bestDist) { bestDist = dd; best = z; }
            }
            if (best != null)
            {
                Vec2 p = best.Def.Center - Toward(best.Def.Center) * (best.Def.Radius * 0.4f);
                if (_sim.CanDeploy(Team, d.Id, p, -1, out _)) return p;
                return best.Def.Center;
            }
            return _sim.HqGatePosition(Team, lane, 20f);
        }

        private Vec2 FrontPoint(int lane)
        {
            var lp = _sim.Map.Lanes[lane];
            int n = lp.Checkpoints.Count;
            for (int k = 0; k < n; k++)
            {
                var cp = Team == Team.Player ? lp.Checkpoints[k] : lp.Checkpoints[n - 1 - k];
                var z = _sim.ZoneById(cp.ZoneId);
                if (z != null && z.Owner != Team) return z.Def.Center;
            }
            var foe = _sim.Map.Layout.GetHq(Foe);
            return foe != null ? foe.Position : Vec2.Zero;
        }

        private bool FindCluster(out Vec2 center, out int count)
        {
            center = Vec2.Zero;
            count = 0;
            foreach (var u in _sim.UnitList)
            {
                if (!u.Alive || u.Team != Foe || !u.Targetable || u.IsAir) continue;
                if (u.Def.Ability == AbilityType.Stealth && !u.Revealed) continue;
                int n = _sim.Spatial.Count(u.Pos, 18f, Foe, false);
                if (u.IsStructure) n += 2;
                if (n > count) { count = n; center = u.Pos; }
            }
            return count > 0;
        }
    }
}
