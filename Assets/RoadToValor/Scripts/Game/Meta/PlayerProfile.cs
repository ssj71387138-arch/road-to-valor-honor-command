using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoadToValor.Game
{
    [Serializable]
    public sealed class MatchRecord
    {
        public string date = "";
        public bool victory;
        public float duration;
        public int kills;
        public int losses;
        public int zones;
        public int buildings;
        public string difficulty = "normal";
        public int goldReward;
        public int xpReward;
    }

    /// <summary>Local player progression (level, currencies, deck, research, records), persisted with PlayerPrefs.</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        private const string Key = "RTV_Profile_v1";

        public string commanderName = "사령관";
        public int level = 1;
        public int xp;
        public int gold = 1500;
        public int gems = 120;
        public int[] deck = new int[0];
        public int[] research = new int[8];
        public List<MatchRecord> records = new List<MatchRecord>();
        public string lastSupplyDate = "";
        public int supplyCrates = 1;
        public string difficulty = "normal";
        public bool snow = true;
        public bool edgeScroll = true;
        public int totalWins;
        public int totalBattles;

        public int XpForNext => 1000 + (level - 1) * 300;

        public static PlayerProfile Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var p = JsonUtility.FromJson<PlayerProfile>(json);
                    if (p != null)
                    {
                        if (p.research == null || p.research.Length < 8) p.research = new int[8];
                        if (p.records == null) p.records = new List<MatchRecord>();
                        return p;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[RTV] profile load failed: " + e.Message); }
            return new PlayerProfile();
        }

        public void Save()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }

        public void AddXp(int amount)
        {
            xp += amount;
            while (xp >= XpForNext)
            {
                xp -= XpForNext;
                level++;
                gems += 20;
            }
        }
    }
}
