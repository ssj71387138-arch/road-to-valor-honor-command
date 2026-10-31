using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public enum OrderType : byte { Idle, Move, AttackMove, AttackUnit, Hold, Lane, ClearRubble }

    /// <summary>High level activity, consumed by the animation state machines in the view layer.</summary>
    public enum UnitActivity : byte { Idle, Moving, Aiming, Firing, Reloading, Ability, Constructing, Dropping, Dead, Victory }

    public enum CommandType : byte { Move, AttackMove, AttackUnit, Stop, Hold, UseAbility, DeployCard, ClearRubble }

    /// <summary>
    /// Player / AI intent. Every gameplay change from input goes through these serializable commands,
    /// so the same stream can later be sent over the network (lockstep) or recorded for replays.
    /// </summary>
    public struct GameCommand
    {
        public CommandType Type;
        public Team Team;
        public int[] UnitIds;
        public Vec2 Target;
        public int TargetUnitId;
        public int DefId;
        public int PadId;

        public static GameCommand Units(CommandType type, Team team, int[] ids, Vec2 target, int targetUnit = 0)
            => new GameCommand { Type = type, Team = team, UnitIds = ids, Target = target, TargetUnitId = targetUnit, PadId = -1 };

        public static GameCommand Deploy(Team team, int defId, Vec2 target, int padId = -1)
            => new GameCommand { Type = CommandType.DeployCard, Team = team, DefId = defId, Target = target, PadId = padId };
    }

    public enum SimEventType : byte
    {
        UnitSpawned, UnitDied, Shot, ProjectileLaunched, Impact, Explosion,
        ZoneOwnerChanged, BuildStarted, BuildCompleted, RubbleCreated, RubbleCleared,
        Heal, Repair, AbilityUsed, AirstrikeFlyby, MissileWarning, MineLaid, MineTriggered, MineCleared,
        TeslaArc, CpGained, CardDeployed, DeployFailed, Notification, GameOver, Dismount,
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public Team Team;
        public int UnitId;
        public int OtherId;
        public int DefId;
        public Vec2 Pos;
        public Vec2 Pos2;
        public float Value;
        public float Value2;
        public DamageType Damage;
        public string Text;
    }

    public sealed class Projectile
    {
        public int Id;
        public Team Team;
        public int SourceId;
        public int SourceDefId;
        public int TargetId;
        public Vec2 From;
        public Vec2 To;
        public float Elapsed;
        public float Duration;
        public float Damage;
        public DamageType Type;
        public float Splash;
        public bool Indirect;
        public bool TargetAir;
        public bool Alive;
    }

    public sealed class Strike
    {
        public Team Team;
        public float Time;
        public Vec2 Pos;
        public float Radius;
        public float Damage;
        public DamageType Type;
        public int SourceDefId;
        public bool Big;
    }

    public sealed class Mine
    {
        public int Id;
        public Team Team;
        public Vec2 Pos;
        public float Damage;
        public float TriggerRadius;
        public bool AntiTankOnly;
        public int OwnerId;
        public bool Alive = true;
        public bool Revealed;
    }

    public enum PadState : byte { Empty, Occupied, Rubble }

    public sealed class BuildPad
    {
        public int Id;
        public int ZoneIndex;
        public int ZoneId;
        public Vec2 Pos;
        public PadState State;
        public int StructureId;
        public float RubbleWork;
        public float RubbleTime;
        public int RubbleDefId;
    }

    public sealed class ZoneState
    {
        public int Index;
        public ZoneDef Def;
        public Team Owner;
        /// <summary>-1 = fully enemy, +1 = fully player, 0 = neutral.</summary>
        public float Control;
        public bool Contested;
        public Team Capturer;
        public int PlayerCount;
        public int EnemyCount;
        public int[] PadIds = new int[0];

        public int Id => Def.Id;
        public float Progress => Math.Abs(Control);
        public Team ControlSide => Control > 0.001f ? Team.Player : (Control < -0.001f ? Team.Enemy : Team.Neutral);
    }

    public sealed class TeamStats
    {
        public int Kills;
        public int Losses;
        public int StructuresBuilt;
        public int StructuresDestroyed;
        public int ZonesCaptured;
        public int UnitsDeployed;
        public float CpSpent;
        public float DamageDealt;
    }

    public sealed class TeamState
    {
        public Team Team;
        public float Cp;
        public int[] Deck = new int[0];
        public readonly Dictionary<int, float> CardCooldowns = new Dictionary<int, float>();
        public readonly TeamStats Stats = new TeamStats();
        public int HqUnitId;

        // modifiers (research / difficulty)
        public float CpRegenMultiplier = 1f;
        public float InfantryHpMultiplier = 1f;
        public float VehicleHpMultiplier = 1f;
        public float AttackMultiplier = 1f;

        // derived every field update
        public int ExtractorCount;
        public float ForwardPostBonus;
        public float ResupplyBonus;
        public float CargoBoostTimer;
        public float CargoBoostMultiplier = 1f;
        public float LastRegenRate;

        public float CardCooldown(int defId) => CardCooldowns.TryGetValue(defId, out var t) ? t : 0f;

        public bool HasCard(int defId)
        {
            foreach (int d in Deck) if (d == defId) return true;
            return false;
        }
    }

    /// <summary>Per-battle options chosen in the lobby.</summary>
    public sealed class BattleSetup
    {
        public int[] PlayerDeck;
        public int[] EnemyDeck;
        public string Difficulty = "normal";
        public bool EnemyAi = true;
        public bool PlayerAi;
        public bool Waves = true;
        public float PlayerInfantryHp = 1f;
        public float PlayerVehicleHp = 1f;
        public float PlayerAttack = 1f;
        public float PlayerCpRegen = 1f;
        public int[] StartingSquads = { 1, 1 };
        public int Seed = 12345;
    }
}
