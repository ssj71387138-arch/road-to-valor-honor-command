using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public enum Team : byte { Neutral = 0, Player = 1, Enemy = 2 }

    public static class TeamUtil
    {
        public static Team Opponent(Team t) => t == Team.Player ? Team.Enemy : (t == Team.Enemy ? Team.Player : Team.Neutral);
        public static Team Parse(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "player": case "blue": case "ally": return Team.Player;
                case "enemy": case "red": return Team.Enemy;
                default: return Team.Neutral;
            }
        }
        public static string NameKo(Team t) => t == Team.Player ? "아군" : (t == Team.Enemy ? "적군" : "중립");
    }

    public enum UnitSection { Infantry = 1, Armor = 2, Elite = 3, Air = 4, Structure = 5, Hardcore = 6, Internal = 9 }
    public enum Rarity { Common, Rare, Hero, Epic, Special, Legendary }
    public enum Mobility { Foot, Wheeled, Tracked, Air, Static, Skill }
    public enum ArmorClass { Infantry = 0, Light = 1, Heavy = 2, Air = 3, Structure = 4 }
    public enum DamageType { None = 0, Bullet, Sniper, AP, HE, Flame, AA, Tesla, Melee }
    public enum AbilityMode { Passive, Auto, Active }

    public enum AbilityType
    {
        None, Charge, Heal, Repair, MineSweep, LayMines, Shield, Suppress, Aura, Recon, Stealth, Transport, Paradrop,
        Suicide, Fear, DroneSwarm, CallArtillery, Burn, Resupply, Airstrike, CarpetBomb, SupplyDrop, CargoDrop, Missile,
        Tesla, SlowField, Fortify, ForwardPost, CpBoost, Flare
    }

    [Flags]
    public enum UnitFlags
    {
        None = 0,
        Indirect = 1,        // artillery: only fires at targets spotted by the team, lobbed shells
        Amphibious = 2,      // can cross rivers / shallow sea
        DeployAnywhere = 4,  // paradrop
        AtMine = 8,          // mines only triggered by vehicles
        Loiter = 16,         // air unit circles its anchor
        HQ = 32,
    }

    /// <summary>
    /// Static definition of one of the 74 cards (+ internal units like HQ).
    /// Identity fields come from unit_roster.csv; all numbers from unit_balance.csv.
    /// </summary>
    public sealed class UnitDefinition
    {
        // --- roster ---
        public int Id;
        public string Key = "";
        public string NameKo = "";
        public UnitSection Section;
        public string BadgeColor = "";
        public Rarity Rarity;
        public string Visual = "";
        public string Role = "";
        public string Description = "";

        // --- balance (proposed) ---
        public int CpCost;
        public float CardCooldown;
        public int Count = 1;
        public float Hp = 100;
        public float Attack;
        public DamageType DamageType;
        public float Range;
        public float AttackInterval = 1f;
        public int Magazine;
        public float ReloadTime;
        public float MoveSpeed;
        public float Sight = 30;
        public ArmorClass Armor;
        public bool HitAir;
        public bool HitGround = true;
        public float Splash;
        public float ProjectileSpeed;
        public Mobility Mobility;
        public float Radius = 0.6f;
        public AbilityType Ability;
        public AbilityMode AbilityMode;
        public float AbP1, AbP2, AbCooldown, AbRadius;
        public float BuildTime;
        public float Lifetime;
        public UnitFlags Flags;
        public bool HasBalanceRow;

        public bool IsStructure => Mobility == Mobility.Static;
        public bool IsAir => Mobility == Mobility.Air;
        public bool IsSkill => Mobility == Mobility.Skill;
        public bool IsGroundMobile => Mobility == Mobility.Foot || Mobility == Mobility.Wheeled || Mobility == Mobility.Tracked;
        public bool IsInfantry => Mobility == Mobility.Foot;
        public bool IsVehicle => Mobility == Mobility.Wheeled || Mobility == Mobility.Tracked;
        public bool IsHQ => (Flags & UnitFlags.HQ) != 0;
        public bool CanAttack => Attack > 0f && DamageType != DamageType.None && Range > 0f;
        public bool Has(UnitFlags f) => (Flags & f) != 0;

        public string RarityKo
        {
            get
            {
                switch (Rarity)
                {
                    case Rarity.Common: return "일반";
                    case Rarity.Rare: return "희귀";
                    case Rarity.Hero: return "영웅";
                    case Rarity.Epic: return "에픽";
                    case Rarity.Special: return "특수";
                    default: return "전설";
                }
            }
        }

        public string SectionKo
        {
            get
            {
                switch (Section)
                {
                    case UnitSection.Infantry: return "보병 부대";
                    case UnitSection.Armor: return "탱크 및 기갑";
                    case UnitSection.Elite: return "특수병 및 정예";
                    case UnitSection.Air: return "항공 및 공군 전술";
                    case UnitSection.Structure: return "구조물 및 방어 타워";
                    case UnitSection.Hardcore: return "하드코어 기갑 및 전술";
                    default: return "내부";
                }
            }
        }

        /// <summary>Card category for deployment rules.</summary>
        public string DeployKindKo
        {
            get
            {
                if (IsStructure) return "건물 (건설 패드)";
                if (IsSkill) return "전술 스킬";
                if (IsAir) return "항공";
                if (Has(UnitFlags.DeployAnywhere)) return "공수 강하";
                return "지상 부대";
            }
        }

        public override string ToString() => "#" + Id + " " + NameKo;
    }
}
