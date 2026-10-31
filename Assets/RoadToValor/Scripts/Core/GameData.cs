namespace RoadToValor.Core
{
    /// <summary>Everything loaded from the data files plus the generated map. Built once per session.</summary>
    public sealed class GameData
    {
        public GameConfig Config;
        public UnitDatabase Units;
        public MapLayout Layout;
        public MapData Map;

        public static GameData Load(string rosterCsv, string balanceCsv, string configJson, string layoutJson)
        {
            var d = new GameData
            {
                Config = GameConfig.Load(configJson),
                Units = UnitDatabase.Load(rosterCsv, balanceCsv),
                Layout = MapLayout.Load(layoutJson),
            };
            d.Map = MapGenerator.Generate(d.Layout, d.Config);
            return d;
        }
    }
}
