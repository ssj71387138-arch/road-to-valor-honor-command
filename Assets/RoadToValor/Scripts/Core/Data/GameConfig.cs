using System;
using System.Collections.Generic;

namespace RoadToValor.Core
{
    public sealed class DifficultySettings
    {
        public float CpRegenMultiplier = 1f;
        public float ThinkInterval = 1.6f;
        public int SkillMinTargets = 5;
    }

    public sealed class ResearchTrack
    {
        public string Key = "";
        public string Name = "";
        public float PerLevel;
        public int MaxLevel;
        public int BaseCost;
    }

    /// <summary>All tunable game rules. Loaded from game_config.json.</summary>
    public sealed class GameConfig
    {
        // map
        public float MapSize = 1200f;
        public int MapSeed = 1945;
        public int HeightResolution = 513;
        public float NavCellSize = 4f;
        public float WaterLevel = 9f;
        public float BaseHeight = 12f;
        public float TerrainMaxHeight = 150f;
        public float TerrainBottom = -10f;

        // CP
        public float CpMax = 10f;
        public float CpStart = 5f;
        public float CpRegenPerSecond = 0.35f;
        public float ExtractorMultiplier = 1.5f;
        public bool ExtractorStacks;
        public float SupplyDropAmount = 3f;
        public int StrategicZoneId = 14;
        public float StrategicZoneRegenBonus = 0.15f;

        // capture
        public float SecondsToCapture = 16f;
        public float PerExtraUnitBonus = 0.2f;
        public float MaxCaptureSpeedMultiplier = 2.5f;
        public float AbandonRevertPerSecond = 0.03f;
        public int MaxBuildingsPerZone = 2;
        public int PadsPerZone = 2;

        // waves
        public bool WavesEnabled = true;
        public float WaveFirstDelay = 6f;
        public float WaveInterval = 32f;
        public int[] WaveBase = { 1, 1, 1, 1, 2, 2 };
        public int[] WaveEvery3rd = { 5 };
        public int[] WaveEvery4th = { 16 };
        public int MaxWaveUnitsPerTeam = 90;

        // limits
        public int MaxUnitsPerTeam = 170;
        public int MaxProjectiles = 500;
        public int MaxMinesPerTeam = 40;

        // deploy
        public float HqDeployRadius = 85f;
        public float ZoneDeployRadiusFactor = 1f;
        public float ParadropMinDistFromEnemyHQ = 140f;

        // rubble
        public float RubbleWorkAmount = 100f;
        public float RubbleAutoClearSeconds;

        // AI
        public string AiDifficulty = "normal";
        public int[] AiDeck = { 1, 2, 4, 5, 8, 18, 58, 49 };
        public int[] PlayerDefaultDeck = { 1, 2, 5, 6, 8, 18, 57, 54 };
        public readonly Dictionary<string, DifficultySettings> Difficulties = new Dictionary<string, DifficultySettings>();

        // [damageType, armorClass]
        public readonly float[,] DamageMatrix = new float[9, 5];

        public readonly List<ResearchTrack> Research = new List<ResearchTrack>();

        public GameConfig()
        {
            for (int d = 0; d < 9; d++) for (int a = 0; a < 5; a++) DamageMatrix[d, a] = d == 0 ? 0f : 1f;
            Difficulties["normal"] = new DifficultySettings();
        }

        public float DamageMultiplier(DamageType dt, ArmorClass ac) => DamageMatrix[(int)dt, (int)ac];

        public DifficultySettings GetDifficulty(string key)
        {
            if (key != null && Difficulties.TryGetValue(key, out var d)) return d;
            return Difficulties.TryGetValue("normal", out var n) ? n : new DifficultySettings();
        }

        public static GameConfig Load(string json)
        {
            var c = new GameConfig();
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("game_config.json root must be an object");

            var map = J.Obj(root, "map");
            c.MapSize = J.F(map, "size", c.MapSize);
            c.MapSeed = J.I(map, "seed", c.MapSeed);
            c.HeightResolution = J.I(map, "heightResolution", c.HeightResolution);
            c.NavCellSize = J.F(map, "navCellSize", c.NavCellSize);
            c.WaterLevel = J.F(map, "waterLevel", c.WaterLevel);
            c.BaseHeight = J.F(map, "baseHeight", c.BaseHeight);
            c.TerrainMaxHeight = J.F(map, "terrainMaxHeight", c.TerrainMaxHeight);
            c.TerrainBottom = J.F(map, "terrainBottom", c.TerrainBottom);

            var cp = J.Obj(root, "cp");
            c.CpMax = J.F(cp, "max", c.CpMax);
            c.CpStart = J.F(cp, "start", c.CpStart);
            c.CpRegenPerSecond = J.F(cp, "regenPerSecond", c.CpRegenPerSecond);
            c.ExtractorMultiplier = J.F(cp, "extractorMultiplier", c.ExtractorMultiplier);
            c.ExtractorStacks = J.B(cp, "extractorStacks", c.ExtractorStacks);
            c.SupplyDropAmount = J.F(cp, "supplyDropAmount", c.SupplyDropAmount);
            c.StrategicZoneId = J.I(cp, "strategicZoneId", c.StrategicZoneId);
            c.StrategicZoneRegenBonus = J.F(cp, "strategicZoneRegenBonus", c.StrategicZoneRegenBonus);

            var cap = J.Obj(root, "capture");
            c.SecondsToCapture = J.F(cap, "secondsToCapture", c.SecondsToCapture);
            c.PerExtraUnitBonus = J.F(cap, "perExtraUnitBonus", c.PerExtraUnitBonus);
            c.MaxCaptureSpeedMultiplier = J.F(cap, "maxSpeedMultiplier", c.MaxCaptureSpeedMultiplier);
            c.AbandonRevertPerSecond = J.F(cap, "abandonRevertPerSecond", c.AbandonRevertPerSecond);
            c.MaxBuildingsPerZone = J.I(cap, "maxBuildingsPerZone", c.MaxBuildingsPerZone);
            c.PadsPerZone = J.I(cap, "padsPerZone", c.PadsPerZone);

            var waves = J.Obj(root, "waves");
            if (waves != null)
            {
                c.WavesEnabled = J.B(waves, "enabled", c.WavesEnabled);
                c.WaveFirstDelay = J.F(waves, "firstDelay", c.WaveFirstDelay);
                c.WaveInterval = J.F(waves, "interval", c.WaveInterval);
                c.WaveBase = J.IntArray(waves, "base");
                c.WaveEvery3rd = J.IntArray(waves, "every3rd");
                c.WaveEvery4th = J.IntArray(waves, "every4th");
                c.MaxWaveUnitsPerTeam = J.I(waves, "maxWaveUnitsPerTeam", c.MaxWaveUnitsPerTeam);
            }

            var lim = J.Obj(root, "limits");
            c.MaxUnitsPerTeam = J.I(lim, "maxUnitsPerTeam", c.MaxUnitsPerTeam);
            c.MaxProjectiles = J.I(lim, "maxProjectiles", c.MaxProjectiles);
            c.MaxMinesPerTeam = J.I(lim, "maxMinesPerTeam", c.MaxMinesPerTeam);

            var dep = J.Obj(root, "deploy");
            c.HqDeployRadius = J.F(dep, "hqRadius", c.HqDeployRadius);
            c.ZoneDeployRadiusFactor = J.F(dep, "zoneRadiusFactor", c.ZoneDeployRadiusFactor);
            c.ParadropMinDistFromEnemyHQ = J.F(dep, "paradropMinDistFromEnemyHQ", c.ParadropMinDistFromEnemyHQ);

            var rub = J.Obj(root, "rubble");
            c.RubbleWorkAmount = J.F(rub, "workAmount", c.RubbleWorkAmount);
            c.RubbleAutoClearSeconds = J.F(rub, "autoClearSeconds", c.RubbleAutoClearSeconds);

            var ai = J.Obj(root, "ai");
            c.AiDifficulty = J.S(ai, "difficulty", c.AiDifficulty);
            var aiDeck = J.IntArray(ai, "deck");
            if (aiDeck.Length > 0) c.AiDeck = aiDeck;
            var pDeck = J.IntArray(root, "playerDefaultDeck");
            if (pDeck.Length > 0) c.PlayerDefaultDeck = pDeck;

            var diffs = J.Obj(root, "difficulty");
            if (diffs != null)
            {
                foreach (var kv in diffs)
                {
                    c.Difficulties[kv.Key] = new DifficultySettings
                    {
                        CpRegenMultiplier = J.F(kv.Value, "cpRegenMultiplier", 1f),
                        ThinkInterval = J.F(kv.Value, "thinkInterval", 1.6f),
                        SkillMinTargets = J.I(kv.Value, "skillMinTargets", 5),
                    };
                }
            }

            var dm = J.Obj(root, "damageMatrix");
            if (dm != null)
            {
                foreach (var kv in dm)
                {
                    DamageType dt = UnitDatabase.ParseDamage(kv.Key);
                    if (dt == DamageType.None && kv.Key != "none") continue;
                    foreach (ArmorClass ac in Enum.GetValues(typeof(ArmorClass)))
                    {
                        string k = ac.ToString().ToLowerInvariant();
                        c.DamageMatrix[(int)dt, (int)ac] = J.F(kv.Value, k, c.DamageMatrix[(int)dt, (int)ac]);
                    }
                }
            }

            var research = J.Obj(root, "research");
            if (research != null)
            {
                foreach (var kv in research)
                {
                    if (kv.Key.StartsWith("_", StringComparison.Ordinal)) continue;
                    c.Research.Add(new ResearchTrack
                    {
                        Key = kv.Key,
                        Name = J.S(kv.Value, "name", kv.Key),
                        PerLevel = J.F(kv.Value, "perLevel"),
                        MaxLevel = J.I(kv.Value, "maxLevel", 5),
                        BaseCost = J.I(kv.Value, "baseCost", 500),
                    });
                }
            }
            return c;
        }
    }
}
