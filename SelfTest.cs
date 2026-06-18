namespace V2rayNMonitor;

public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var failures = new List<string>();
        Check(Paths.DataDir.EndsWith("ProxyMonitor", StringComparison.OrdinalIgnoreCase), "uses ProxyMonitor data directory", failures);
        Check(!Paths.DataDir.EndsWith("V2rayNMonitor", StringComparison.OrdinalIgnoreCase), "does not use old data directory", failures);
        Check(ReferenceEquals(UiColors.ForScore(90), UiColors.Excellent), "score 90 uses excellent color", failures);
        Check(ReferenceEquals(UiColors.ForScore(75), UiColors.Good), "score 75 uses good color", failures);
        Check(ReferenceEquals(UiColors.ForScore(60), UiColors.Fair), "score 60 uses fair color", failures);
        Check(ReferenceEquals(UiColors.ForScore(45), UiColors.Watch), "score 45 uses watch color", failures);
        Check(ReferenceEquals(UiColors.ForScore(25), UiColors.Poor), "score 25 uses poor color", failures);
        Check(ReferenceEquals(UiColors.ForScore(10), UiColors.Bad), "score 10 uses bad color", failures);
        Check(ReferenceEquals(UiColors.ForScore(0), UiColors.Empty), "score 0 uses empty color", failures);
        Check(Math.Abs(HistoryStore.MedianForTest([1, 2, 100, 101]) - 51) < .000001, "even median averages middle values", failures);

        var emptyDelay = new NodeScore { Name = "empty" };
        var validDelay = new NodeScore { Name = "valid", Samples = 1, MedianDelay = 120 };
        Check(new NodeScoreComparer(nameof(NodeScore.MedianDelay), System.ComponentModel.ListSortDirection.Ascending).Compare(emptyDelay, validDelay) > 0, "empty delay sorts last ascending", failures);
        Check(new NodeScoreComparer(nameof(NodeScore.MedianDelay), System.ComponentModel.ListSortDirection.Descending).Compare(emptyDelay, validDelay) > 0, "empty delay sorts last descending", failures);
        var emptySpeed = new NodeScore { Name = "empty-speed", Samples = 1 };
        var validSpeed = new NodeScore { Name = "valid-speed", Samples = 1, SpeedSamples = 1, MedianSpeed = 1024 * 1024 };
        Check(new NodeScoreComparer(nameof(NodeScore.MedianSpeed), System.ComponentModel.ListSortDirection.Descending).Compare(emptySpeed, validSpeed) > 0, "empty speed sorts last", failures);

        var ready = new NodeScore { Samples = 24, SpeedSamples = 3, MedianSpeed = QualityThresholds.MinUsefulSpeedBytesPerSecond, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 };
        var sparse = new NodeScore { Samples = 23, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 };
        var unreliable = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .8, RecentSuccessRate = .95, RecentFailures = 0 };
        var recentlyUnreliable = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .8, RecentFailures = 0 };
        var failing = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 2 };
        var badHistoryRecovered = new NodeScore { Samples = 24, SpeedSamples = 0, SuccessRate = .60, RecentSuccessRate = 1, RecentMedianDelay = 120, RecentFailures = 0 };
        var goodHistoryRecovered = new NodeScore { Samples = 24, SpeedSamples = 0, SuccessRate = .89, RecentSuccessRate = 1, RecentMedianDelay = 120, RecentFailures = 0 };
        Check(ready.DataEnough, "formal recommendation threshold passes", failures);
        Check(new NodeScore { Samples = 24, SpeedSamples = 0, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 }.StableEnough, "stable recommendation does not require speed sample", failures);
        Check(goodHistoryRecovered.StableEnough, "recent recovery re-enters stable candidates above floor", failures);
        Check(!badHistoryRecovered.StableEnough, "recent recovery blocks historically unreliable nodes", failures);
        Check(new NodeScore { Samples = 79, SpeedSamples = 0, SuccessRate = .94, RecentSuccessRate = .94, RecentFailures = 0 }.Status.Contains("3"), "status shows missing speed sample count", failures);
        Check(new NodeScore { Samples = 10, SpeedSamples = 0, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 }.Status.Contains("14"), "status shows missing delay sample count", failures);
        Check(!sparse.DataEnough && !unreliable.DataEnough && !recentlyUnreliable.DataEnough && !failing.DataEnough, "sample and reliability gates work", failures);

        var incumbent = new NodeScore { NodeId = "old", CombinedScore = 80, StabilityScore = 80, SpeedScore = 80 };
        var close = new NodeScore { NodeId = "close", CombinedScore = 83.9, StabilityScore = 82.9, SpeedScore = 84.9 };
        var clear = new NodeScore { NodeId = "clear", CombinedScore = 84.1, StabilityScore = 83.1, SpeedScore = 85.1 };
        Check(RecommendationSelector.Select("old", [incumbent, close], x => x.CombinedScore, 4)?.NodeId == "old", "combined recommendation resists small jumps", failures);
        Check(RecommendationSelector.Select("old", [incumbent, clear], x => x.CombinedScore, 4)?.NodeId == "clear", "combined recommendation replaces on clear lead", failures);
        Check(RecommendationSelector.Select("old", [clear], x => x.CombinedScore, 4)?.NodeId == "clear", "combined recommendation replaces missing current", failures);
        var fastOld = new NodeScore { NodeId = "fast-old", MedianSpeed = 1_000_000 };
        var fastClose = new NodeScore { NodeId = "fast-close", MedianSpeed = 1_090_000 };
        var fastClear = new NodeScore { NodeId = "fast-clear", MedianSpeed = 1_110_000 };
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastOld, fastClose], x => x.MedianSpeed, .10)?.NodeId == "fast-old", "fast recommendation resists small jumps", failures);
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastOld, fastClear], x => x.MedianSpeed, .10)?.NodeId == "fast-clear", "fast recommendation replaces on clear lead", failures);
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastClear], x => x.MedianSpeed, .10)?.NodeId == "fast-clear", "fast recommendation replaces missing current", failures);

        foreach (var test in CompatibilityTests.Run())
            Check(test.Passed, $"compatibility matrix: {test.Name} ({test.Detail})", failures);
        foreach (var test in await ClientAdapterTests.RunAsync())
            Check(test.Passed, $"client adapter: {test.Name} ({test.Detail})", failures);

        var oldConfig = System.Text.Json.Nodes.JsonNode.Parse("""{"indexId":"old"}""")!.AsObject();
        Switcher.UpdateIndexId(oldConfig, "new");
        Check(oldConfig["indexId"]?.GetValue<string>() == "new" && !oldConfig.ContainsKey("IndexId"), "6.x lower-case index switch key", failures);
        var newConfig = System.Text.Json.Nodes.JsonNode.Parse("""{"IndexId":"old"}""")!.AsObject();
        Switcher.UpdateIndexId(newConfig, "new");
        Check(newConfig["IndexId"]?.GetValue<string>() == "new" && !newConfig.ContainsKey("indexId"), "7.x upper-case index switch key", failures);

        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var adapters = ClientDiscovery.Discover(settings);
        Check(adapters.Select(x => x.Id).Distinct().Count() == adapters.Count, "adapter ids are unique", failures);
        var v2ray = adapters.First(x => x.Id == "v2rayn");
        if (v2ray.IsDetected)
        {
            var nodes = await v2ray.GetNodesAsync();
            Check(nodes.Count > 0, "v2rayN read-only node discovery", failures);
            Check(nodes.All(x => x.Id.StartsWith("v2rayn:", StringComparison.Ordinal)), "cross-client node id isolation", failures);
            var compatibility = V2rayNReader.Inspect(settings.V2rayNPath);
            Check(compatibility.CanRead, $"local v2rayN {compatibility.Version} layout compatible", failures);
        }

        foreach (var failure in failures) Console.WriteLine($"FAIL {failure}");
        Console.WriteLine(failures.Count == 0 ? "PASS all self-tests" : $"FAIL {failures.Count} self-tests");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string name, List<string> failures)
    {
        if (condition) Console.WriteLine($"PASS {name}");
        else failures.Add(name);
    }
}
