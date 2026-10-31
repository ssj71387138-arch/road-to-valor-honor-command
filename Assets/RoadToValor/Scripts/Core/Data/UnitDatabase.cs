using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    /// <summary>Loads unit_roster.csv + unit_balance.csv into UnitDefinitions keyed by id.</summary>
    public sealed class UnitDatabase
    {
        public const int HqDefId = 900;
        public const int FirstCardId = 1;
        public const int LastCardId = 74;

        private readonly Dictionary<int, UnitDefinition> _byId = new Dictionary<int, UnitDefinition>();
        public readonly List<UnitDefinition> Cards = new List<UnitDefinition>();
        public readonly List<string> Warnings = new List<string>();

        public IEnumerable<UnitDefinition> All => _byId.Values;
        public int Count => _byId.Count;

        public UnitDefinition Get(int id) => _byId.TryGetValue(id, out var d) ? d : null;
        public bool Contains(int id) => _byId.ContainsKey(id);

        public static UnitDatabase Load(string rosterCsv, string balanceCsv)
        {
            var db = new UnitDatabase();
            foreach (var row in Csv.Parse(rosterCsv))
            {
                int id = Csv.I(row, "id", -1);
                if (id < 0) continue;
                var d = new UnitDefinition
                {
                    Id = id,
                    Key = Csv.S(row, "key"),
                    NameKo = Csv.S(row, "name_ko"),
                    Section = ParseSection(Csv.S(row, "section")),
                    BadgeColor = Csv.S(row, "badge_color"),
                    Rarity = ParseRarity(Csv.S(row, "rarity")),
                    Visual = Csv.S(row, "visual"),
                    Role = Csv.S(row, "role"),
                    Description = Csv.S(row, "description"),
                };
                db._byId[id] = d;
            }

            foreach (var row in Csv.Parse(balanceCsv))
            {
                int id = Csv.I(row, "id", -1);
                if (!db._byId.TryGetValue(id, out var d))
                {
                    db.Warnings.Add("balance row for unknown unit id " + id);
                    continue;
                }
                d.HasBalanceRow = true;
                d.CpCost = Csv.I(row, "cp");
                d.CardCooldown = Csv.F(row, "card_cd");
                d.Count = Math.Max(1, Csv.I(row, "count", 1));
                d.Hp = Csv.F(row, "hp", 100);
                d.Attack = Csv.F(row, "atk");
                d.DamageType = ParseDamage(Csv.S(row, "dmg"));
                d.Range = Csv.F(row, "range");
                d.AttackInterval = Math.Max(0.05f, Csv.F(row, "interval", 1f));
                d.Magazine = Csv.I(row, "mag");
                d.ReloadTime = Csv.F(row, "reload");
                d.MoveSpeed = Csv.F(row, "speed");
                d.Sight = Csv.F(row, "sight", 30);
                d.Armor = ParseArmor(Csv.S(row, "armor"));
                d.HitAir = Csv.I(row, "air") != 0;
                d.HitGround = Csv.I(row, "ground", 1) != 0;
                d.Splash = Csv.F(row, "splash");
                d.ProjectileSpeed = Csv.F(row, "proj");
                d.Mobility = ParseMobility(Csv.S(row, "mobility"));
                d.Radius = Csv.F(row, "radius", 0.6f);
                d.Ability = ParseAbility(Csv.S(row, "ability"));
                d.AbilityMode = ParseMode(Csv.S(row, "ab_mode"));
                d.AbP1 = Csv.F(row, "ab_p1");
                d.AbP2 = Csv.F(row, "ab_p2");
                d.AbCooldown = Csv.F(row, "ab_cd");
                d.AbRadius = Csv.F(row, "ab_radius");
                d.BuildTime = Csv.F(row, "build_time");
                d.Lifetime = Csv.F(row, "lifetime");
                d.Flags = ParseFlags(Csv.S(row, "flags"));
                if (d.Id == HqDefId) d.Flags |= UnitFlags.HQ;
            }

            foreach (var d in db._byId.Values)
            {
                if (!d.HasBalanceRow) db.Warnings.Add("unit " + d.Id + " has no balance row (using defaults)");
                if (d.Id >= FirstCardId && d.Id <= LastCardId) db.Cards.Add(d);
                if (d.CpCost < 0 || d.CpCost > 10) db.Warnings.Add("unit " + d.Id + " cp cost out of range 0..10");
            }
            db.Cards.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int id = FirstCardId; id <= LastCardId; id++)
                if (!db._byId.ContainsKey(id)) db.Warnings.Add("card id " + id + " missing from roster");
            return db;
        }

        public static UnitSection ParseSection(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "infantry": return UnitSection.Infantry;
                case "armor": return UnitSection.Armor;
                case "elite": return UnitSection.Elite;
                case "air": return UnitSection.Air;
                case "structure": return UnitSection.Structure;
                case "hardcore": return UnitSection.Hardcore;
                default: return UnitSection.Internal;
            }
        }

        public static Rarity ParseRarity(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "common": return Rarity.Common;
                case "rare": return Rarity.Rare;
                case "hero": return Rarity.Hero;
                case "epic": return Rarity.Epic;
                case "special": return Rarity.Special;
                default: return Rarity.Legendary;
            }
        }

        public static DamageType ParseDamage(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "bullet": return DamageType.Bullet;
                case "sniper": return DamageType.Sniper;
                case "ap": return DamageType.AP;
                case "he": return DamageType.HE;
                case "flame": return DamageType.Flame;
                case "aa": return DamageType.AA;
                case "tesla": return DamageType.Tesla;
                case "melee": return DamageType.Melee;
                default: return DamageType.None;
            }
        }

        public static ArmorClass ParseArmor(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "light": return ArmorClass.Light;
                case "heavy": return ArmorClass.Heavy;
                case "air": return ArmorClass.Air;
                case "structure": return ArmorClass.Structure;
                default: return ArmorClass.Infantry;
            }
        }

        public static Mobility ParseMobility(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "wheeled": return Mobility.Wheeled;
                case "tracked": return Mobility.Tracked;
                case "air": return Mobility.Air;
                case "static": return Mobility.Static;
                case "skill": return Mobility.Skill;
                default: return Mobility.Foot;
            }
        }

        public static AbilityMode ParseMode(string s)
        {
            switch (s.Trim().ToLowerInvariant())
            {
                case "auto": return AbilityMode.Auto;
                case "active": return AbilityMode.Active;
                default: return AbilityMode.Passive;
            }
        }

        public static AbilityType ParseAbility(string s)
        {
            s = s.Trim().ToLowerInvariant();
            foreach (AbilityType t in Enum.GetValues(typeof(AbilityType)))
                if (t.ToString().ToLowerInvariant() == s) return t;
            return AbilityType.None;
        }

        public static UnitFlags ParseFlags(string s)
        {
            var f = UnitFlags.None;
            foreach (string part in s.Split('|', ' ', ';'))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "indirect": f |= UnitFlags.Indirect; break;
                    case "amphibious": f |= UnitFlags.Amphibious; break;
                    case "deploy_anywhere": f |= UnitFlags.DeployAnywhere; break;
                    case "atmine": f |= UnitFlags.AtMine; break;
                    case "loiter": f |= UnitFlags.Loiter; break;
                    case "hq": f |= UnitFlags.HQ; break;
                }
            }
            return f;
        }

        public static string AbilityNameKo(AbilityType t)
        {
            switch (t)
            {
                case AbilityType.Charge: return "돌격";
                case AbilityType.Heal: return "치료";
                case AbilityType.Repair: return "수리·잔해 철거";
                case AbilityType.MineSweep: return "지뢰 탐지·해체";
                case AbilityType.LayMines: return "지뢰 매설";
                case AbilityType.Shield: return "방탄 방패";
                case AbilityType.Suppress: return "제압 사격";
                case AbilityType.Aura: return "지휘 오라";
                case AbilityType.Recon: return "정찰·은신 탐지";
                case AbilityType.Stealth: return "은신";
                case AbilityType.Transport: return "병력 하차";
                case AbilityType.Paradrop: return "공수 강하";
                case AbilityType.Suicide: return "자폭";
                case AbilityType.Fear: return "심리전";
                case AbilityType.DroneSwarm: return "드론 발진";
                case AbilityType.CallArtillery: return "포격 요청";
                case AbilityType.Burn: return "화염 지속 피해";
                case AbilityType.Resupply: return "보급 지원";
                case AbilityType.Airstrike: return "정밀 폭격";
                case AbilityType.CarpetBomb: return "융단 폭격";
                case AbilityType.SupplyDrop: return "보급 투하 (CP +3)";
                case AbilityType.CargoDrop: return "CP 충전 가속";
                case AbilityType.Missile: return "전술 미사일";
                case AbilityType.Tesla: return "연쇄 전격";
                case AbilityType.SlowField: return "감속 지대";
                case AbilityType.Fortify: return "엄폐 강화";
                case AbilityType.ForwardPost: return "전방 배치 거점";
                case AbilityType.CpBoost: return "CP 충전 1.5배";
                case AbilityType.Flare: return "조명탄 지원";
                default: return "없음";
            }
        }
    }
}
