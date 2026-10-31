using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RoadToValor.Core;

/// <summary>
/// Headless tests for the engine-independent simulation. Run: dotnet run -c Release --project Tools/CoreTests
/// Exit code 0 = all passed.
/// </summary>
public static class Program
{
    private static int _failures;
    private static int _passes;
    private static GameData _data;

    private static void Check(bool cond, string what)
    {
        if (cond) { _passes++; Console.WriteLine("  [PASS] " + what); }
        else { _failures++; Console.WriteLine("  [FAIL] " + what); }
    }

    public static int Main(string[] args)
    {
        string root = FindRepoRoot();
        string dataDir = Path.Combine(root, "Assets/RoadToValor/Resources/RoadToValor/Data");
        var sw = Stopwatch.StartNew();
        _data = GameData.Load(
            File.ReadAllText(Path.Combine(dataDir, "unit_roster.csv")),
            File.ReadAllText(Path.Combine(dataDir, "unit_balance.csv")),
            File.ReadAllText(Path.Combine(dataDir, "game_config.json")),
            File.ReadAllText(Path.Combine(dataDir, "map_layout.json")));
        Console.WriteLine("Data + map generation: " + sw.ElapsedMilliseconds + " ms");

        if (args.Length > 0 && args[0] == "diag") { Diagnose(); return 0; }
        Run("Data", TestData);
        Run("Map", TestMap);
        Run("Milestone: select infantry, move, fight, capture", TestMilestone);
        Run("Construction / rubble / 3rd build blocked", TestConstruction);
        Run("CP rules", TestCp);
        Run("Player order overrides auto AI", TestOrderPriority);
        Run("All 74 cards deploy and run", TestAllCards);
        Run("Abilities", TestAbilities);
        Run("AI vs AI full battle", TestAiBattle);
        Run("Performance 240 units", TestPerformance);

        Console.WriteLine();
        Console.WriteLine("RESULT: " + _passes + " passed, " + _failures + " failed");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action a)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name);
        try { a(); }
        catch (Exception e) { _failures++; Console.WriteLine("  [FAIL] exception: " + e); }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets/RoadToValor"))) dir = dir.Parent;
        if (dir == null) throw new Exception("repo root not found");
        return dir.FullName;
    }

    private static BattleSimulation NewSim(int[] deck = null, bool waves = false, bool enemyAi = false, bool playerAi = false, int[] starting = null)
    {
        return new BattleSimulation(_data, new BattleSetup
        {
            PlayerDeck = deck ?? _data.Config.PlayerDefaultDeck,
            Waves = waves,
            EnemyAi = enemyAi,
            PlayerAi = playerAi,
            StartingSquads = starting ?? new int[0],
            Seed = 7,
        });
    }

    private static void Steps(BattleSimulation sim, float seconds)
    {
        int n = (int)(seconds / BattleSimulation.Dt);
        for (int i = 0; i < n; i++) { sim.Step(); sim.Events.Clear(); }
    }

    // ---------------------------------------------------------------------------------------------

    private static void TestData()
    {
        var u = _data.Units;
        Check(u.Cards.Count == 74, "74 cards registered (" + u.Cards.Count + ")");
        Check(u.Get(900) != null && u.Get(900).IsHQ, "HQ definition present");
        Check(u.Warnings.Count == 0, "no data warnings" + (u.Warnings.Count > 0 ? ": " + string.Join("; ", u.Warnings) : ""));
        Check(u.Cards.All(c => c.HasBalanceRow), "every card has a balance row");
        Check(u.Cards.All(c => c.CpCost >= 0 && c.CpCost <= 10), "all CP costs within 0..10");
        Check(u.Cards.Count(c => c.Section == UnitSection.Infantry) == 15, "1-15 infantry");
        Check(u.Cards.Count(c => c.Section == UnitSection.Armor) == 15, "16-30 armor");
        Check(u.Cards.Count(c => c.Section == UnitSection.Elite) == 15, "31-45 elite");
        Check(u.Cards.Count(c => c.Section == UnitSection.Air) == 10, "46-55 air");
        Check(u.Cards.Count(c => c.Section == UnitSection.Structure) == 10, "56-65 structures");
        Check(u.Cards.Count(c => c.Section == UnitSection.Hardcore) == 9, "66-74 hardcore");
        Check(u.Cards.Where(c => c.Section == UnitSection.Structure).All(c => c.IsStructure), "56-65 are buildable structures");
        Check(u.Get(1).NameKo == "기초 소총수" && u.Get(74).NameKo == "야간 조명 드론", "names from roster image");
        Check(u.Get(65).Ability == AbilityType.CpBoost && u.Get(54).Ability == AbilityType.SupplyDrop, "extractor / supply drop abilities wired");
        Check(_data.Config.CpMax == 10f && _data.Config.ExtractorMultiplier == 1.5f && _data.Config.SupplyDropAmount == 3f, "spec CP rules in config");
    }

    private static void TestMap()
    {
        var m = _data.Map;
        Check(m.Size == 1200f, "map is 1200m x 1200m");
        Check(m.Layout.Zones.Count == 24, "24 capture zones");
        Check(m.Layout.Zones.Select(z => z.Id).Distinct().Count() == 24, "unique zone ids");
        Check(m.Layout.Zones.All(z => z.PadPositions.Length == 2), "exactly 2 pads per zone");
        var sim = NewSim();
        Check(sim.Pads.Length == 48, "48 build pads in simulation (" + sim.Pads.Length + ")");
        Check(m.Lanes.Count == 3, "3 lanes (top/mid/bot)");
        foreach (var lane in m.Lanes)
        {
            var phq = m.Layout.GetHq(Team.Player).Position;
            var ehq = m.Layout.GetHq(Team.Enemy).Position;
            Check(Vec2.Distance(lane.Points[0], phq) < 30f && Vec2.Distance(lane.Points[lane.Count - 1], ehq) < 30f,
                "lane " + lane.Def.Key + " connects both HQs (" + lane.Count + " pts, checkpoints " + string.Join(",", lane.Checkpoints.Select(c => c.ZoneId)) + ")");
            bool walkable = true;
            for (int i = 1; i < lane.Count; i++) if (!m.Nav.LineWalkable(lane.Points[i - 1], lane.Points[i], false)) { walkable = false; break; }
            Check(walkable, "lane " + lane.Def.Key + " is fully walkable");
        }
        var pf = new Pathfinder(m.Nav);
        foreach (Team t in new[] { Team.Player, Team.Enemy })
        {
            var hq = m.Layout.GetHq(t).Position;
            Vec2 start = sim.HqGatePosition(t, 1, 20f);
            int reachable = 0;
            var bad = new List<int>();
            foreach (var z in m.Layout.Zones)
            {
                var path = pf.FindPath(start, z.Center, false);
                if (path != null && Vec2.Distance(path[path.Count - 1], z.Center) < z.Radius * 0.5f) reachable++;
                else bad.Add(z.Id);
            }
            Check(reachable == 24, t + " HQ reaches all 24 zone centres" + (bad.Count > 0 ? " (unreachable: " + string.Join(",", bad) + ")" : ""));
        }
        int walk = 0, water = 0, oob = 0;
        foreach (var f in m.Nav.Flags)
        {
            if ((f & NavFlags.OutOfBounds) != 0) oob++;
            else if ((f & NavFlags.Water) != 0) water++;
            else walk++;
        }
        Console.WriteLine("    nav cells: walkable-ish " + walk + ", water " + water + ", out-of-bounds " + oob + ", props " + m.Props.Count);
        Check(m.Props.Count > 800, "battlefield dressed with props (" + m.Props.Count + ")");
        foreach (var z in m.Layout.Zones)
            foreach (var pad in z.PadPositions)
                if (m.SampleHeight(pad) < m.WaterLevel + 0.2f) Check(false, "pad of zone " + z.Id + " underwater");
        float minH = float.MaxValue, maxH = float.MinValue;
        foreach (float h in m.Heights) { minH = Math.Min(minH, h); maxH = Math.Max(maxH, h); }
        Console.WriteLine("    height range " + minH.ToString("0.0") + " .. " + maxH.ToString("0.0") + " m");
        Check(maxH > 80f && minH < m.WaterLevel, "snow mountains and sea present");
        var splat = m.ComputeSplat(64);
        Check(splat.GetLength(0) == 64 && splat.GetLength(2) == 8, "splat map computes 8 layers");
    }

    private static void TestMilestone()
    {
        var sim = NewSim(starting: new[] { 1 });
        var mine = sim.UnitList.Where(u => u.Team == Team.Player && u.Def.Id == 1).ToList();
        Check(mine.Count == 4, "player starts with a selectable rifle squad of 4");
        var z14 = sim.ZoneById(14);
        Check(z14.Owner == Team.Neutral, "zone 14 starts neutral");
        // Clear the enemy starting squad so the move is uncontested.
        foreach (var u in sim.UnitList.Where(u => u.Team == Team.Enemy && !u.IsStructure).ToList()) sim.Kill(u, null, true);

        sim.Enqueue(GameCommand.Units(CommandType.Move, Team.Player, mine.Select(u => u.Id).ToArray(), z14.Def.Center));
        float t0 = sim.Time;
        float arrive = -1f;
        for (int i = 0; i < 20 * 240 && arrive < 0f; i++)
        {
            sim.Step(); sim.Events.Clear();
            if (mine.All(u => Vec2.Distance(u.Pos, z14.Def.Center) < z14.Def.Radius * 0.8f)) arrive = sim.Time - t0;
        }
        Check(arrive > 0f, "squad walked ~490m to zone 14 in " + arrive.ToString("0.0") + "s");
        float t1 = sim.Time;
        for (int i = 0; i < 20 * 40 && z14.Owner != Team.Player; i++) { sim.Step(); sim.Events.Clear(); }
        Check(z14.Owner == Team.Player, "zone 14 captured by player in " + (sim.Time - t1).ToString("0.0") + "s");

        // Enemy counter-attack: contested => progress frozen, then combat.
        var foeDef = _data.Units.Get(1);
        var foes = sim.SpawnSquad(foeDef, Team.Enemy, z14.Def.Center + new Vec2(0, 30), false);
        foreach (var f in foes) { f.Order = OrderType.AttackMove; f.OrderPos = z14.Def.Center; }
        float ctrlBefore = z14.Control;
        bool sawContested = false, sawShot = false;
        for (int i = 0; i < 20 * 60; i++)
        {
            sim.Step();
            if (sim.Events.Any(e => e.Type == SimEventType.Shot)) sawShot = true;
            sim.Events.Clear();
            if (z14.Contested) sawContested = true;
            if (foes.All(f => !f.Alive) || mine.All(m => !m.Alive)) break;
        }
        Check(sawShot, "units auto-acquired and fired at enemies");
        Check(sawContested, "zone became contested during the fight");
        int deadFoes = foes.Count(f => !f.Alive), deadMine = mine.Count(m => !m.Alive);
        Check(deadFoes + deadMine > 0, "combat produced casualties (enemy dead " + deadFoes + ", ours dead " + deadMine + ")");
    }

    private static void TestConstruction()
    {
        var sim = NewSim(deck: new[] { 57, 58, 8, 65, 1, 2, 3, 4 });
        var ts = sim.TeamOf(Team.Player);
        var z18 = sim.ZoneById(18);
        ts.Cp = 10f;
        Check(sim.TryDeploy(Team.Player, 57, z18.Def.Center, z18.PadIds[0], out string r1), "build MG bunker on zone 18 pad A " + r1);
        ts.Cp = 10f;
        Check(sim.TryDeploy(Team.Player, 58, z18.Def.Center, z18.PadIds[1], out string r2), "build AT turret on zone 18 pad B " + r2);
        ts.Cp = 10f;
        Check(!sim.TryDeploy(Team.Player, 65, z18.Def.Center, z18.PadIds[0], out string r3), "3rd building in zone 18 blocked: " + r3);
        Check(sim.StructuresInZone(z18.Index) == 2, "zone holds exactly 2 buildings");
        var enemyZone = sim.ZoneById(4);
        ts.Cp = 10f;
        Check(!sim.TryDeploy(Team.Player, 65, enemyZone.Def.Center, enemyZone.PadIds[0], out string r4), "cannot build in enemy zone: " + r4);
        var neutralZone = sim.ZoneById(14);
        Check(!sim.TryDeploy(Team.Player, 65, neutralZone.Def.Center, neutralZone.PadIds[0], out string r5), "cannot build in neutral zone: " + r5);

        Steps(sim, 8f);
        var bunker = sim.Get(sim.Pads[z18.PadIds[0]].StructureId);
        Check(bunker != null && !bunker.Constructing, "bunker finished construction");
        sim.Kill(bunker, null);
        sim.Events.Clear();
        Check(sim.Pads[z18.PadIds[0]].State == PadState.Rubble, "destroyed bunker leaves rubble");
        ts.Cp = 10f;
        Check(!sim.TryDeploy(Team.Player, 65, z18.Def.Center, z18.PadIds[0], out string r6), "cannot build on rubble: " + r6);

        ts.Cp = 10f;
        Check(sim.TryDeploy(Team.Player, 8, z18.Def.Center + new Vec2(0, -8), -1, out _), "deploy engineer platoon");
        var eng = sim.LastDeployed.Select(u => u.Id).ToArray();
        sim.Enqueue(new GameCommand { Type = CommandType.ClearRubble, Team = Team.Player, UnitIds = eng, PadId = z18.PadIds[0], Target = sim.Pads[z18.PadIds[0]].Pos });
        float t0 = sim.Time;
        for (int i = 0; i < 20 * 60 && sim.Pads[z18.PadIds[0]].State == PadState.Rubble; i++) { sim.Step(); sim.Events.Clear(); }
        Check(sim.Pads[z18.PadIds[0]].State == PadState.Empty, "engineers cleared rubble in " + (sim.Time - t0).ToString("0.0") + "s");
        ts.Cp = 10f;
        Check(sim.TryDeploy(Team.Player, 65, z18.Def.Center, z18.PadIds[0], out string r7), "rebuild after clearing " + r7);
    }

    private static void TestCp()
    {
        var sim = NewSim(deck: new[] { 54, 65, 1, 2, 3, 4, 5, 6 });
        var ts = sim.TeamOf(Team.Player);
        ts.Cp = 0f;
        Steps(sim, 1f);
        float baseRate = ts.LastRegenRate;
        Check(Math.Abs(baseRate - _data.Config.CpRegenPerSecond) < 0.001f, "base regen = config (" + baseRate + "/s)");
        Steps(sim, 60f);
        Check(Math.Abs(ts.Cp - 10f) < 0.001f, "CP caps at 10 (" + ts.Cp + ")");
        ts.Cp = 9f;
        Check(sim.TryDeploy(Team.Player, 54, Vec2.Zero, -1, out _), "supply drop usable at 0 CP cost");
        Check(Math.Abs(ts.Cp - 10f) < 0.001f, "supply drop +3 clamped to 10 (" + ts.Cp + ")");
        Check(!sim.TryDeploy(Team.Player, 54, Vec2.Zero, -1, out string cd), "supply drop has card cooldown: " + cd);
        ts.Cp = 2f;
        Check(!sim.TryDeploy(Team.Player, 65, Vec2.Zero, sim.ZoneById(22).PadIds[0], out string lowCp), "insufficient CP blocks card: " + lowCp);
        ts.Cp = 10f;
        Check(sim.TryDeploy(Team.Player, 65, Vec2.Zero, sim.ZoneById(22).PadIds[0], out _), "build resource extractor");
        Steps(sim, 10f);
        Steps(sim, 0.6f);
        Check(Math.Abs(ts.LastRegenRate - baseRate * 1.5f) < 0.001f, "extractor gives 1.5x regen (" + ts.LastRegenRate.ToString("0.000") + ")");
        sim.AddCp(Team.Player, -50f);
        Check(ts.Cp == 0f, "CP never below 0");
    }

    private static void TestOrderPriority()
    {
        var sim = NewSim(starting: new int[0]);
        var me = sim.SpawnSquad(_data.Units.Get(1), Team.Player, new Vec2(0, -60), false);
        var foes = sim.SpawnSquad(_data.Units.Get(1), Team.Enemy, new Vec2(0, -40), false);
        foreach (var f in foes) f.Order = OrderType.Hold;
        foreach (var u in me) u.PlayerControlled = true;
        sim.Enqueue(GameCommand.Units(CommandType.Move, Team.Player, me.Select(u => u.Id).ToArray(), new Vec2(0, -200)));
        bool firedWhileMoving = false;
        for (int i = 0; i < 20 * 6; i++)
        {
            sim.Step();
            foreach (var e in sim.Events) if (e.Type == SimEventType.Shot && e.Team == Team.Player) firedWhileMoving = true;
            sim.Events.Clear();
        }
        Check(!firedWhileMoving, "infantry under move order ignore enemies (player intent first)");
        Check(me.All(u => u.Pos.Z < -70f), "squad retreated away from the enemy");
        sim.Enqueue(GameCommand.Units(CommandType.AttackUnit, Team.Player, me.Select(u => u.Id).ToArray(), foes[0].Pos, foes[0].Id));
        bool shotTarget = false;
        for (int i = 0; i < 20 * 20; i++)
        {
            sim.Step();
            foreach (var e in sim.Events) if (e.Type == SimEventType.Shot && e.Team == Team.Player && e.OtherId == foes[0].Id) shotTarget = true;
            sim.Events.Clear();
        }
        Check(shotTarget, "attack order focuses the chosen target");
    }

    private static void TestAllCards()
    {
        int ok = 0;
        var failures = new List<string>();
        foreach (var card in _data.Units.Cards)
        {
            var sim = NewSim(deck: new[] { card.Id }, starting: new int[0]);
            var ts = sim.TeamOf(Team.Player);
            ts.Cp = 10f;
            var z = sim.ZoneById(18);
            Vec2 pos = card.Has(UnitFlags.DeployAnywhere) ? new Vec2(0, -20) : (card.IsSkill || card.IsAir ? new Vec2(0, 20) : z.Def.Center);
            int pad = card.IsStructure ? z.PadIds[0] : -1;
            // enemies to fight
            sim.SpawnSquad(_data.Units.Get(1), Team.Enemy, new Vec2(0, 20), false);
            sim.SpawnSquad(_data.Units.Get(18), Team.Enemy, new Vec2(-20, 30), false);
            if (!sim.TryDeploy(Team.Player, card.Id, pos, pad, out string reason)) { failures.Add(card.Id + " " + card.NameKo + ": " + reason); continue; }
            if (!card.IsStructure && !card.IsSkill)
            {
                var ids = sim.LastDeployed.Select(u => u.Id).ToArray();
                if (card.IsGroundMobile && ids.Length > 0) sim.Enqueue(GameCommand.Units(CommandType.AttackMove, Team.Player, ids, new Vec2(0, 10)));
            }
            Steps(sim, 25f);
            ok++;
        }
        Check(ok == 74, "all 74 cards deployed and simulated 25s without errors (" + ok + ")" + (failures.Count > 0 ? " failures: " + string.Join(" | ", failures) : ""));
    }

    private static void TestAbilities()
    {
        // Medic heals.
        {
            var sim = NewSim(starting: new int[0]);
            var medic = sim.SpawnUnit(_data.Units.Get(6), Team.Player, new Vec2(0, -300), 0);
            var hurt = sim.SpawnUnit(_data.Units.Get(1), Team.Player, new Vec2(3, -300), 0);
            hurt.Hp = 20f;
            Steps(sim, 5f);
            Check(hurt.Hp > 50f, "medic heals nearby infantry (" + hurt.Hp.ToString("0") + " hp)");
        }
        // Stealth sniper can't be targeted until revealed.
        {
            var sim = NewSim(starting: new int[0]);
            var ghost = sim.SpawnUnit(_data.Units.Get(34), Team.Player, new Vec2(0, -300), 0);
            ghost.Order = OrderType.Hold;
            var foe = sim.SpawnUnit(_data.Units.Get(1), Team.Enemy, new Vec2(0, -270), MathUtil.Pi);
            foe.Order = OrderType.Hold;
            Steps(sim, BattleSimulation.Dt);
            Check(!sim.CanTarget(foe, ghost, false), "ghost sniper is hidden at 30m before firing");
            Steps(sim, 4f);
            Check(sim.CanTarget(foe, ghost, false) || !foe.Alive, "ghost sniper is revealed after firing");
        }
        // Mines.
        {
            var sim = NewSim(starting: new int[0]);
            var layer = sim.SpawnUnit(_data.Units.Get(29), Team.Player, new Vec2(-110, -260), 0);
            layer.Order = OrderType.Move; layer.OrderPos = new Vec2(-110, -150);
            Steps(sim, 20f);
            Check(sim.Mines.Count > 0, "minelayer laid " + sim.Mines.Count + " mines");
        }
        // Artillery needs spotting.
        {
            var sim = NewSim(starting: new int[0]);
            var spg = sim.SpawnUnit(_data.Units.Get(21), Team.Player, new Vec2(0, -250), 0);
            spg.Order = OrderType.Hold;
            var foe = sim.SpawnUnit(_data.Units.Get(18), Team.Enemy, new Vec2(0, -130), MathUtil.Pi);
            foe.Order = OrderType.Hold;
            Steps(sim, 4f);
            float hpNoSpot = foe.Hp;
            var scout = sim.SpawnUnit(_data.Units.Get(15), Team.Player, new Vec2(0, -175), 0);
            scout.Order = OrderType.Hold;
            Steps(sim, 12f);
            Check(hpNoSpot == foe.MaxHp && foe.Hp < foe.MaxHp, "SPG only fires once a scout spots the target");
        }
        // Transport dismount.
        {
            var sim = NewSim(deck: new[] { 26 }, starting: new int[0]);
            sim.TeamOf(Team.Player).Cp = 10;
            sim.TryDeploy(Team.Player, 26, sim.ZoneById(18).Def.Center, -1, out _);
            var apc = sim.LastDeployed[0];
            int before = sim.CountUnits(Team.Player, false);
            sim.Enqueue(GameCommand.Units(CommandType.UseAbility, Team.Player, new[] { apc.Id }, apc.Pos));
            Steps(sim, 0.2f);
            Check(sim.CountUnits(Team.Player, false) == before + 4, "APC dismounts 4 riflemen");
        }
        // V2 missile.
        {
            var sim = NewSim(deck: new[] { 72 }, starting: new int[0]);
            sim.TeamOf(Team.Player).Cp = 10;
            var foes = sim.SpawnSquad(_data.Units.Get(1), Team.Enemy, new Vec2(0, 100), false);
            foreach (var f in foes) f.Order = OrderType.Hold;
            sim.TryDeploy(Team.Player, 72, new Vec2(0, 100), -1, out _);
            Steps(sim, 6f);
            Check(foes.All(f => !f.Alive), "V2 missile wipes the target area after warning");
        }
    }

    private static void TestAiBattle()
    {
        var sim = NewSim(waves: true, enemyAi: true, playerAi: true, starting: new[] { 1 });
        var sw = Stopwatch.StartNew();
        int maxUnits = 0;
        int ownerChanges = 0, deploys = 0, builds = 0;
        float minutes = 12f;
        int steps = (int)(minutes * 60f / BattleSimulation.Dt);
        double worst = 0;
        for (int i = 0; i < steps && !sim.IsOver; i++)
        {
            var t = Stopwatch.GetTimestamp();
            sim.Step();
            double ms = (Stopwatch.GetTimestamp() - t) * 1000.0 / Stopwatch.Frequency;
            if (ms > worst) worst = ms;
            foreach (var e in sim.Events)
            {
                if (e.Type == SimEventType.ZoneOwnerChanged) ownerChanges++;
                if (e.Type == SimEventType.CardDeployed) deploys++;
                if (e.Type == SimEventType.BuildCompleted) builds++;
            }
            sim.Events.Clear();
            maxUnits = Math.Max(maxUnits, sim.UnitList.Count);
            if (i % (int)(60 / BattleSimulation.Dt) == 0)
                Console.WriteLine("    t=" + sim.Time.ToString("0") + "s units=" + sim.UnitList.Count + " zones P/E " + sim.ZonesOwned(Team.Player) + "/" + sim.ZonesOwned(Team.Enemy) +
                                  " HQ " + (sim.Hq(Team.Player)?.Hp ?? 0).ToString("0") + "/" + (sim.Hq(Team.Enemy)?.Hp ?? 0).ToString("0") +
                                  " CP " + sim.TeamOf(Team.Player).Cp.ToString("0.0") + "/" + sim.TeamOf(Team.Enemy).Cp.ToString("0.0") +
                                  " AI: " + sim.Commanders[0].LastDecision);
        }
        double avg = sw.Elapsed.TotalMilliseconds / Math.Max(1, sim.Tick);
        Console.WriteLine("    ticks " + sim.Tick + ", avg " + avg.ToString("0.00") + " ms/tick, worst " + worst.ToString("0.0") + " ms, peak units " + maxUnits + ", paths " + sim.PathsComputedTotal);
        Console.WriteLine("    over=" + sim.IsOver + " winner=" + sim.Winner + " deploys=" + deploys + " ownerChanges=" + ownerChanges + " builds=" + builds);
        Console.WriteLine("    stats P kills " + sim.TeamOf(Team.Player).Stats.Kills + " E kills " + sim.TeamOf(Team.Enemy).Stats.Kills);
        Check(deploys > 20, "AI commanders deployed cards (" + deploys + ")");
        Check(ownerChanges > 0, "zones changed hands (" + ownerChanges + ")");
        Check(maxUnits >= 100, "100+ simultaneous units reached (" + maxUnits + ")");
        Check(avg < 10.0, "average tick under 10 ms (" + avg.ToString("0.00") + ")");
    }

    private static void TestPerformance()
    {
        var sim = NewSim(starting: new int[0]);
        var rng = new Rng(3);
        for (int i = 0; i < 30; i++)
        {
            var a = sim.SpawnSquad(_data.Units.Get(1), Team.Player, new Vec2(rng.Range(-200, 200), rng.Range(-260, -160)), false);
            var b = sim.SpawnSquad(_data.Units.Get(1), Team.Enemy, new Vec2(rng.Range(-200, 200), rng.Range(160, 260)), false);
            foreach (var u in a) { u.Order = OrderType.AttackMove; u.OrderPos = new Vec2(u.Pos.X * 0.5f, 250); }
            foreach (var u in b) { u.Order = OrderType.AttackMove; u.OrderPos = new Vec2(u.Pos.X * 0.5f, -250); }
        }
        int start = sim.UnitList.Count;
        var sw = Stopwatch.StartNew();
        Steps(sim, 60f);
        double avg = sw.Elapsed.TotalMilliseconds / (60f / BattleSimulation.Dt);
        Console.WriteLine("    " + start + " units -> " + sim.UnitList.Count + " after 60s, avg " + avg.ToString("0.00") + " ms/tick (20 Hz budget 50 ms)");
        Check(start >= 240 && avg < 8.0, "240 units simulate under 8 ms/tick");
    }

    private static void Diagnose()
    {
        var sim = NewSim(waves: true, enemyAi: true, playerAi: true, starting: new[] { 1 });
        for (int m = 1; m <= 8; m++)
        {
            Steps(sim, 60f);
            var groups = sim.UnitList.Where(u => !u.IsStructure).GroupBy(u => u.Team + "/" + u.Order + "/" + (u.HoldZoneIndex >= 0 ? "hold" + sim.Zones[u.HoldZoneIndex].Id : "-") + "/lane" + u.LaneIndex)
                .OrderByDescending(g => g.Count()).Take(12);
            Console.WriteLine("t=" + sim.Time.ToString("0") + " units " + sim.UnitList.Count);
            foreach (var g in groups)
            {
                int still = g.Count(u => u.Velocity.Length < 0.2f && u.TargetId == 0);
                Console.WriteLine("   " + g.Key + " n=" + g.Count() + " still/no-target=" + still + " avgPos=" + new Vec2(g.Average(u => u.Pos.X), g.Average(u => u.Pos.Z)));
            }
            Console.WriteLine("   zones: " + string.Join(" ", sim.Zones.Select(z => z.Id + ":" + (z.Owner == Team.Player ? "P" : z.Owner == Team.Enemy ? "E" : "N") + (z.Contested ? "!" : "") + z.PlayerCount + "/" + z.EnemyCount)));
        }
    }
}
