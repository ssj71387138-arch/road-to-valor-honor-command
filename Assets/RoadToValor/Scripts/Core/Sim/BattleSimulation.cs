using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>
    /// Authoritative, engine-independent battle simulation (fixed 20 Hz tick).
    /// The Unity layer only renders its state, reads its events and sends GameCommands.
    /// Split into partial files: Units (movement/orders), Combat, Abilities, Deploy, World (zones, CP, waves).
    /// </summary>
    public sealed partial class BattleSimulation
    {
        public const float Dt = 0.05f;

        public readonly GameConfig Config;
        public readonly UnitDatabase Units;
        public readonly MapData Map;
        public readonly BattleSetup Setup;

        public readonly List<SimUnit> UnitList = new List<SimUnit>(512);
        private readonly Dictionary<int, SimUnit> _units = new Dictionary<int, SimUnit>(512);
        public readonly ZoneState[] Zones;
        public readonly BuildPad[] Pads;
        public readonly TeamState[] Teams = new TeamState[3];
        public readonly List<Projectile> Projectiles = new List<Projectile>(256);
        public readonly List<Strike> Strikes = new List<Strike>(32);
        public readonly List<Mine> Mines = new List<Mine>(64);
        public readonly List<SimEvent> Events = new List<SimEvent>(256);
        public readonly List<EnemyCommander> Commanders = new List<EnemyCommander>();

        public readonly SpatialHash Spatial;
        private readonly Pathfinder _pathfinder;
        private readonly Queue<int> _pathQueue = new Queue<int>();
        public readonly Rng Rng;

        public float Time { get; private set; }
        public int Tick { get; private set; }
        public bool IsOver { get; private set; }
        public Team Winner { get; private set; }
        public int PathsComputedTotal;

        private int _nextId = 1;
        private int _nextSquad = 1;
        private int _nextProjectileId = 1;
        private int _nextMineId = 1;
        private float _waveTimer;
        private int _waveCount;
        private float _captureTimer;
        private float _fieldTimer;
        private readonly List<GameCommand> _commandQueue = new List<GameCommand>();
        private readonly List<SimUnit> _scratchA = new List<SimUnit>(64);
        private readonly List<SimUnit> _scratchB = new List<SimUnit>(64);
        private readonly List<SimUnit> _spawnBuffer = new List<SimUnit>(32);

        public BattleSimulation(GameData data, BattleSetup setup)
        {
            Config = data.Config;
            Units = data.Units;
            Map = data.Map;
            Setup = setup ?? new BattleSetup();
            Rng = new Rng(Setup.Seed);
            Spatial = new SpatialHash(Map.Size + 64f, 24f);
            _pathfinder = new Pathfinder(Map.Nav);

            for (int t = 0; t < 3; t++) Teams[t] = new TeamState { Team = (Team)t, Cp = Config.CpStart };
            Teams[1].Deck = (Setup.PlayerDeck != null && Setup.PlayerDeck.Length > 0) ? Setup.PlayerDeck : Config.PlayerDefaultDeck;
            Teams[2].Deck = (Setup.EnemyDeck != null && Setup.EnemyDeck.Length > 0) ? Setup.EnemyDeck : Config.AiDeck;
            var diff = Config.GetDifficulty(Setup.Difficulty);
            Teams[2].CpRegenMultiplier = diff.CpRegenMultiplier;
            Teams[1].CpRegenMultiplier = Setup.PlayerCpRegen;
            Teams[1].InfantryHpMultiplier = Setup.PlayerInfantryHp;
            Teams[1].VehicleHpMultiplier = Setup.PlayerVehicleHp;
            Teams[1].AttackMultiplier = Setup.PlayerAttack;

            // Zones and build pads (exactly PadsPerZone per zone => 48 on the default map).
            var layoutZones = Map.Layout.Zones;
            Zones = new ZoneState[layoutZones.Count];
            var pads = new List<BuildPad>();
            for (int i = 0; i < layoutZones.Count; i++)
            {
                var zd = layoutZones[i];
                var zs = new ZoneState
                {
                    Index = i,
                    Def = zd,
                    Owner = zd.InitialOwner,
                    Control = zd.InitialOwner == Team.Player ? 1f : (zd.InitialOwner == Team.Enemy ? -1f : 0f),
                };
                int padCount = Math.Min(Config.PadsPerZone, zd.PadPositions.Length);
                zs.PadIds = new int[padCount];
                for (int p = 0; p < padCount; p++)
                {
                    var pad = new BuildPad { Id = pads.Count, ZoneIndex = i, ZoneId = zd.Id, Pos = zd.PadPositions[p], State = PadState.Empty };
                    zs.PadIds[p] = pad.Id;
                    pads.Add(pad);
                }
                Zones[i] = zs;
            }
            Pads = pads.ToArray();

            // HQs.
            var hqDef = Units.Get(UnitDatabase.HqDefId);
            foreach (var hq in Map.Layout.Hqs)
            {
                if (hqDef == null) break;
                var u = SpawnUnit(hqDef, hq.Team, hq.Position, hq.Team == Team.Player ? 0f : MathUtil.Pi);
                Teams[(int)hq.Team].HqUnitId = u.Id;
            }

            // Starting squads at each HQ's mid gate.
            foreach (int defId in Setup.StartingSquads ?? new int[0])
            {
                var def = Units.Get(defId);
                if (def == null) continue;
                foreach (Team team in new[] { Team.Player, Team.Enemy })
                {
                    Vec2 gate = HqGatePosition(team, 1, 22f);
                    var spawned = SpawnSquad(def, team, gate, false);
                    foreach (var u in spawned)
                    {
                        if (team == Team.Enemy || Setup.PlayerAi) AssignLane(u, 1);
                        else u.PlayerControlled = true;
                    }
                }
            }

            if (Setup.EnemyAi) Commanders.Add(new EnemyCommander(this, Team.Enemy, diff));
            if (Setup.PlayerAi) Commanders.Add(new EnemyCommander(this, Team.Player, Config.GetDifficulty("normal")));
            _waveTimer = Config.WaveFirstDelay;
        }

        // ------------------------------------------------------------------ public API

        public SimUnit Get(int id) => id != 0 && _units.TryGetValue(id, out var u) ? u : null;
        public TeamState TeamOf(Team t) => Teams[(int)t];
        public SimUnit Hq(Team t) => Get(Teams[(int)t].HqUnitId);

        public void Enqueue(GameCommand cmd) => _commandQueue.Add(cmd);

        public int CountUnits(Team team, bool includeStructures = true)
        {
            int n = 0;
            foreach (var u in UnitList)
                if (u.Alive && u.Team == team && (includeStructures || !u.IsStructure)) n++;
            return n;
        }

        public int ZonesOwned(Team team)
        {
            int n = 0;
            foreach (var z in Zones) if (z.Owner == team) n++;
            return n;
        }

        public ZoneState ZoneById(int id)
        {
            foreach (var z in Zones) if (z.Def.Id == id) return z;
            return null;
        }

        public ZoneState ZoneAt(Vec2 p)
        {
            foreach (var z in Zones)
                if (Vec2.DistanceSq(p, z.Def.Center) <= z.Def.Radius * z.Def.Radius) return z;
            return null;
        }

        public BuildPad PadNear(Vec2 p, float maxDist)
        {
            BuildPad best = null;
            float bd = maxDist * maxDist;
            foreach (var pad in Pads)
            {
                float d = Vec2.DistanceSq(pad.Pos, p);
                if (d <= bd) { bd = d; best = pad; }
            }
            return best;
        }

        public int StructuresInZone(int zoneIndex)
        {
            int n = 0;
            foreach (int pid in Zones[zoneIndex].PadIds)
                if (Pads[pid].State == PadState.Occupied) n++;
            return n;
        }

        public void Emit(SimEvent e) => Events.Add(e);

        public void Notify(Team team, string text, Vec2 pos)
            => Events.Add(new SimEvent { Type = SimEventType.Notification, Team = team, Text = text, Pos = pos });

        public Vec2 HqGatePosition(Team team, int laneIndex, float extra)
        {
            var hq = Map.Layout.GetHq(team);
            if (hq == null) return Vec2.Zero;
            if (Map.HqGateYaws.TryGetValue((int)team, out var yaws) && laneIndex >= 0 && laneIndex < yaws.Length)
            {
                Vec2 p = hq.Position + Vec2.FromYaw(yaws[laneIndex]) * (hq.Radius * 0.78f + extra);
                if (Map.Nav.NearestWalkable(p, false, 8, out Vec2 w)) return w;
                return p;
            }
            return hq.Position;
        }

        // ------------------------------------------------------------------ main loop

        public void Step()
        {
            Time += Dt;
            Tick++;
            if (IsOver)
            {
                foreach (var u in UnitList) if (u.Alive && u.Team == Winner) u.Activity = UnitActivity.Victory;
                return;
            }

            for (int i = 0; i < _commandQueue.Count; i++) ExecuteCommand(_commandQueue[i]);
            _commandQueue.Clear();

            UpdateCp(Dt);
            if (Setup.Waves && Config.WavesEnabled) UpdateWaves(Dt);
            foreach (var c in Commanders) c.Update(Dt);

            Spatial.Clear();
            foreach (var u in UnitList) if (u.Alive) Spatial.Insert(u);

            _fieldTimer -= Dt;
            if (_fieldTimer <= 0f) { _fieldTimer = 0.5f; UpdateFields(); }

            ProcessPathQueue();

            for (int i = 0; i < UnitList.Count; i++)
            {
                var u = UnitList[i];
                if (!u.Alive) continue;
                u.PrevPos = u.Pos;
                UpdateUnit(u, Dt);
            }

            UpdateProjectiles(Dt);
            UpdateStrikes();
            UpdateMines();

            _captureTimer -= Dt;
            if (_captureTimer <= 0f) { _captureTimer = 0.25f; UpdateCapture(0.25f); }

            UpdateRubble(Dt);

            // Spawn queued units (dismounts, drones) and remove the dead.
            if (_spawnBuffer.Count > 0)
            {
                foreach (var s in _spawnBuffer) { UnitList.Add(s); _units[s.Id] = s; }
                _spawnBuffer.Clear();
            }
            for (int i = UnitList.Count - 1; i >= 0; i--)
            {
                var u = UnitList[i];
                if (!u.Alive) { UnitList.RemoveAt(i); _units.Remove(u.Id); }
            }

            CheckVictory();
        }

        private void CheckVictory()
        {
            var p = Hq(Team.Player);
            var e = Hq(Team.Enemy);
            Team winner = Team.Neutral;
            if (e == null || !e.Alive) winner = Team.Player;
            else if (p == null || !p.Alive) winner = Team.Enemy;
            if (winner == Team.Neutral) return;
            IsOver = true;
            Winner = winner;
            Events.Add(new SimEvent { Type = SimEventType.GameOver, Team = winner, Text = winner == Team.Player ? "승리" : "패배" });
            foreach (var u in UnitList) if (u.Alive && u.Team == winner) u.Activity = UnitActivity.Victory;
        }

        // ------------------------------------------------------------------ commands

        private void ExecuteCommand(GameCommand c)
        {
            switch (c.Type)
            {
                case CommandType.DeployCard:
                    if (!TryDeploy(c.Team, c.DefId, c.Target, c.PadId, out string reason))
                        Events.Add(new SimEvent { Type = SimEventType.DeployFailed, Team = c.Team, DefId = c.DefId, Pos = c.Target, Text = reason });
                    return;
            }
            if (c.UnitIds == null) return;

            // Formation offsets for group move orders.
            int count = 0;
            Vec2 centroid = Vec2.Zero;
            foreach (int id in c.UnitIds)
            {
                var u = Get(id);
                if (u == null || !u.Alive || u.Team != c.Team || u.IsStructure) continue;
                centroid += u.Pos;
                count++;
            }
            if (count == 0 && c.Type != CommandType.UseAbility && c.Type != CommandType.Stop && c.Type != CommandType.Hold) return;
            if (count > 0) centroid /= count;
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));
            int slot = 0;
            Vec2 facing = (c.Target - centroid).Normalized;
            if (facing.SqrLength < 0.1f) facing = new Vec2(0, 1);

            foreach (int id in c.UnitIds)
            {
                var u = Get(id);
                if (u == null || !u.Alive || u.Team != c.Team) continue;
                if (u.IsStructure && c.Type != CommandType.UseAbility) continue;
                u.PlayerControlled = c.Team == Team.Player && !Setup.PlayerAi;
                u.LaneIndex = u.IsWaveUnit && !u.PlayerControlled ? u.LaneIndex : -1;
                u.HoldZoneIndex = -1;

                Vec2 offset = Vec2.Zero;
                if (count > 1)
                {
                    int row = slot / cols, col = slot % cols;
                    float spacing = Math.Max(2.6f, u.Radius * 2.6f);
                    float lx = (col - (cols - 1) * 0.5f) * spacing;
                    float lz = -row * spacing;
                    offset = new Vec2(lx, lz).RotateYaw(facing.ToYaw());
                }
                if (!u.IsStructure) slot++;

                switch (c.Type)
                {
                    case CommandType.Move:
                        SetOrder(u, OrderType.Move, c.Target + offset);
                        break;
                    case CommandType.AttackMove:
                        SetOrder(u, OrderType.AttackMove, c.Target + offset);
                        break;
                    case CommandType.AttackUnit:
                        u.Order = OrderType.AttackUnit;
                        u.OrderTargetId = c.TargetUnitId;
                        u.TargetId = c.TargetUnitId;
                        u.Path = null;
                        break;
                    case CommandType.Stop:
                        u.Order = OrderType.Idle;
                        u.Anchor = u.Pos;
                        u.Path = null;
                        u.TargetId = 0;
                        break;
                    case CommandType.Hold:
                        u.Order = OrderType.Hold;
                        u.Anchor = u.Pos;
                        u.Path = null;
                        break;
                    case CommandType.UseAbility:
                        TriggerActiveAbility(u);
                        break;
                    case CommandType.ClearRubble:
                        if (u.Def.Ability == AbilityType.Repair && c.PadId >= 0 && c.PadId < Pads.Length)
                        {
                            u.Order = OrderType.ClearRubble;
                            u.OrderPadId = c.PadId;
                            u.OrderPos = Pads[c.PadId].Pos;
                            u.Path = null;
                        }
                        else SetOrder(u, OrderType.Move, c.Target + offset);
                        break;
                }
            }
        }

        private void SetOrder(SimUnit u, OrderType type, Vec2 target)
        {
            u.Order = type;
            u.OrderPos = Map.ClampToMap(target, 6f);
            u.OrderTargetId = 0;
            u.Path = null;
            u.PathIndex = 0;
            u.BlockedTicks = 0;
            if (type == OrderType.Move) u.TargetId = 0;
        }

        // ------------------------------------------------------------------ spawning / death

        public SimUnit SpawnUnit(UnitDefinition def, Team team, Vec2 pos, float yaw, bool deferred = false)
        {
            var ts = Teams[(int)team];
            float hpMult = def.IsInfantry ? ts.InfantryHpMultiplier : (def.IsVehicle ? ts.VehicleHpMultiplier : 1f);
            var u = new SimUnit
            {
                Id = _nextId++,
                Def = def,
                Team = team,
                Pos = pos,
                PrevPos = pos,
                Yaw = yaw,
                TurretYaw = yaw,
                MaxHp = def.Hp * hpMult,
                Ammo = def.Magazine,
                SpawnTime = Time,
                Anchor = pos,
                OrderPos = pos,
                Lifetime = def.Lifetime,
                RetargetTimer = (_nextId % 7) * 0.05f,
            };
            u.Hp = u.MaxHp;
            if (deferred) _spawnBuffer.Add(u);
            else { UnitList.Add(u); _units[u.Id] = u; }
            Events.Add(new SimEvent { Type = SimEventType.UnitSpawned, Team = team, UnitId = u.Id, DefId = def.Id, Pos = pos });
            return u;
        }

        public List<SimUnit> SpawnSquad(UnitDefinition def, Team team, Vec2 center, bool deferred)
        {
            var result = new List<SimUnit>(def.Count);
            int squad = _nextSquad++;
            int cols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(def.Count)));
            float spacing = Math.Max(2.4f, def.Radius * 2.8f);
            float yaw = team == Team.Player ? 0f : MathUtil.Pi;
            for (int i = 0; i < def.Count; i++)
            {
                int row = i / cols, col = i % cols;
                Vec2 p = center + new Vec2((col - (cols - 1) * 0.5f) * spacing, (row - 0.5f) * spacing).RotateYaw(yaw);
                if (def.IsGroundMobile && !Map.Nav.IsWalkable(p, def.Has(UnitFlags.Amphibious)))
                    Map.Nav.NearestWalkable(p, def.Has(UnitFlags.Amphibious), 10, out p);
                var u = SpawnUnit(def, team, p, yaw, deferred);
                u.SquadId = squad;
                result.Add(u);
            }
            return result;
        }

        public void Kill(SimUnit u, SimUnit killer, bool silent = false)
        {
            if (!u.Alive) return;
            u.Alive = false;
            u.Hp = 0f;
            u.DeathTime = Time;
            u.Activity = UnitActivity.Dead;
            if (!silent)
            {
                Teams[(int)u.Team].Stats.Losses++;
                if (killer != null && killer.Team != u.Team)
                {
                    killer.Kills++;
                    var ks = Teams[(int)killer.Team].Stats;
                    ks.Kills++;
                    if (u.IsStructure) ks.StructuresDestroyed++;
                }
            }
            Events.Add(new SimEvent { Type = SimEventType.UnitDied, Team = u.Team, UnitId = u.Id, OtherId = killer?.Id ?? 0, DefId = u.Def.Id, Pos = u.Pos, Value = silent ? 1f : 0f });

            if (u.PadId >= 0 && u.PadId < Pads.Length)
            {
                var pad = Pads[u.PadId];
                if (pad.StructureId == u.Id)
                {
                    pad.StructureId = 0;
                    pad.State = PadState.Rubble;
                    pad.RubbleWork = Config.RubbleWorkAmount;
                    pad.RubbleTime = Time;
                    pad.RubbleDefId = u.Def.Id;
                    Events.Add(new SimEvent { Type = SimEventType.RubbleCreated, Team = u.Team, UnitId = pad.Id, DefId = u.Def.Id, Pos = pad.Pos });
                }
            }

            // Ground transports spill their passengers when destroyed.
            if (u.Def.Ability == AbilityType.Transport && u.PassengerCount > 0 && u.IsGround)
                Dismount(u, 0.5f);

            if (u.Def.IsHQ)
                Events.Add(new SimEvent { Type = SimEventType.Explosion, Team = u.Team, Pos = u.Pos, Value = 30f, Value2 = 2f });
        }

        public float AddCp(Team team, float amount)
        {
            var ts = Teams[(int)team];
            float before = ts.Cp;
            ts.Cp = MathUtil.Clamp(ts.Cp + amount, 0f, Config.CpMax);
            return ts.Cp - before;
        }
    }
}
