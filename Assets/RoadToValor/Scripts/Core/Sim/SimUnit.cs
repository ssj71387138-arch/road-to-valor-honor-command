using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>Runtime state of one unit / structure / aircraft. Pure data: logic lives in BattleSimulation.</summary>
    public sealed class SimUnit
    {
        public int Id;
        public UnitDefinition Def;
        public Team Team;
        public int SquadId;

        // transform
        public Vec2 Pos;
        public Vec2 PrevPos;
        public Vec2 Velocity;
        public float Yaw;
        public float TurretYaw;

        // health
        public float Hp;
        public float MaxHp;
        public bool Alive = true;
        public float SpawnTime;
        public float DeathTime;
        /// <summary>0 = destroyed in combat, 1 = left the battlefield (aircraft exit / lifetime), 2 = self-destructed.</summary>
        public byte DeathKind;

        // combat
        public float AttackCooldown;
        public int Ammo;
        public float ReloadTimer;
        public int TargetId;
        public float RetargetTimer;
        public float LastFireTime = -999f;
        public float LastHitTime = -999f;
        public Vec2 AimPoint;
        public int Kills;
        public float DamageDealt;

        // orders
        public OrderType Order;
        public Vec2 OrderPos;
        public int OrderTargetId;
        public int OrderPadId = -1;
        public bool PlayerControlled;
        public Vec2 Anchor;

        // pathing
        public List<Vec2> Path;
        public int PathIndex;
        public bool PathPending;
        public Vec2 PathGoal;
        public float RepathTimer;
        public int BlockedTicks;

        // lane AI
        public int LaneIndex = -1;
        public int LaneDir = 1;
        public int LaneProgress;
        public int HoldZoneIndex = -1;
        public Vec2 HoldPoint;
        public bool IsWaveUnit;

        // status effects (recomputed by field updates every 0.5s)
        public float AuraAttackBonus;
        public float AuraDefense;
        public float FearAttackMult = 1f;
        public float FearSpeedMult = 1f;
        public float SlowMult = 1f;
        public float FlareBonus;
        public bool Revealed;
        public readonly bool[] SpottedBy = new bool[3];

        public float SuppressTimer;
        public float SuppressMult = 1f;
        public float BurnTimer;
        public float BurnDps;
        public float ChargeTimer;

        // abilities
        public float AbilityCooldown;
        public float AbilityTick;
        public float AbilityActiveTimer;
        public int PassengerCount;
        public float Lifetime;
        public float DropTimer;
        public int OwnerUnitId;
        public bool Exiting;

        // structures
        public int PadId = -1;
        public bool Constructing;
        public float BuildProgress = 1f;

        // presentation hints
        public UnitActivity Activity;
        public float ActivityTimer;
        public float FireFlashTime = -999f;

        public bool IsAir => Def.IsAir;
        public bool IsStructure => Def.IsStructure;
        public bool IsGround => Def.IsGroundMobile;
        public bool IsInfantry => Def.IsInfantry;
        public bool IsVehicle => Def.IsVehicle;
        public bool Amphibious => Def.Has(UnitFlags.Amphibious);
        public float Radius => Def.Radius;
        public float HpRatio => MaxHp > 0f ? Hp / MaxHp : 0f;
        public bool Targetable => Alive && DropTimer <= 0f && !Exiting;
        public bool HasTurret => Def.Mobility == Mobility.Tracked || (Def.IsStructure && Def.CanAttack);

        public override string ToString() => Def.NameKo + "#" + Id + "(" + Team + ")";
    }
}
