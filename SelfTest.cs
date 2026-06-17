namespace V2rayNMonitor;

public static class SelfTest
{
    public static async Task<int> RunAsync()
    {
        var failures = new List<string>();
        Check(Paths.DataDir.EndsWith("ProxyMonitor", StringComparison.OrdinalIgnoreCase), "独立数据目录", failures);
        Check(!Paths.DataDir.EndsWith("V2rayNMonitor", StringComparison.OrdinalIgnoreCase), "未使用旧版数据目录", failures);
        Check(ReferenceEquals(UiColors.ForScore(90), UiColors.Excellent), "优秀分显示深绿", failures);
        Check(ReferenceEquals(UiColors.ForScore(75), UiColors.Good), "良好分显示浅绿", failures);
        Check(ReferenceEquals(UiColors.ForScore(60), UiColors.Fair), "中等分显示浅黄", failures);
        Check(ReferenceEquals(UiColors.ForScore(45), UiColors.Watch), "观察分显示橙黄", failures);
        Check(ReferenceEquals(UiColors.ForScore(25), UiColors.Poor), "偏低分显示浅橙红", failures);
        Check(ReferenceEquals(UiColors.ForScore(10), UiColors.Bad), "低分显示红色", failures);
        Check(ReferenceEquals(UiColors.ForScore(0), UiColors.Empty), "无数据显示灰色", failures);
        Check(Math.Abs(HistoryStore.MedianForTest([1, 2, 100, 101]) - 51) < .000001, "偶数样本中位数取两侧平均", failures);

        var emptyDelay = new NodeScore { Name = "empty" };
        var validDelay = new NodeScore { Name = "valid", Samples = 1, MedianDelay = 120 };
        Check(new NodeScoreComparer(nameof(NodeScore.MedianDelay), System.ComponentModel.ListSortDirection.Ascending).Compare(emptyDelay, validDelay) > 0, "排序升序时空延迟排最后", failures);
        Check(new NodeScoreComparer(nameof(NodeScore.MedianDelay), System.ComponentModel.ListSortDirection.Descending).Compare(emptyDelay, validDelay) > 0, "排序降序时空延迟排最后", failures);
        var emptySpeed = new NodeScore { Name = "empty-speed", Samples = 1 };
        var validSpeed = new NodeScore { Name = "valid-speed", Samples = 1, SpeedSamples = 1, MedianSpeed = 1024 * 1024 };
        Check(new NodeScoreComparer(nameof(NodeScore.MedianSpeed), System.ComponentModel.ListSortDirection.Descending).Compare(emptySpeed, validSpeed) > 0, "排序时空速度排最后", failures);

        var ready = new NodeScore { Samples = 24, SpeedSamples = 3, MedianSpeed = QualityThresholds.MinUsefulSpeedBytesPerSecond, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 };
        var sparse = new NodeScore { Samples = 23, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 };
        var unreliable = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .8, RecentSuccessRate = .95, RecentFailures = 0 };
        var recentlyUnreliable = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .8, RecentFailures = 0 };
        var failing = new NodeScore { Samples = 24, SpeedSamples = 3, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 2 };
        Check(ready.DataEnough, "正式推荐门槛通过", failures);
        Check(new NodeScore { Samples = 24, SpeedSamples = 0, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 }.StableEnough, "稳定推荐不依赖速度样本", failures);
        Check(new NodeScore { Samples = 24, SpeedSamples = 0, SuccessRate = .87, RecentSuccessRate = 1, RecentMedianDelay = 120, RecentFailures = 0 }.StableEnough, "近期恢复节点重新进入稳定候选", failures);
        Check(new NodeScore { Samples = 79, SpeedSamples = 0, SuccessRate = .94, RecentSuccessRate = .94, RecentFailures = 0 }.Status == "还差 3 次有效测速", "状态显示缺少的测速次数", failures);
        Check(new NodeScore { Samples = 10, SpeedSamples = 0, SuccessRate = .95, RecentSuccessRate = .95, RecentFailures = 0 }.Status == "还差 14 延迟 / 3 测速", "状态显示缺少的延迟与测速次数", failures);
        Check(!sparse.DataEnough && !unreliable.DataEnough && !recentlyUnreliable.DataEnough && !failing.DataEnough, "样本与近期可靠性门槛生效", failures);
        var incumbent = new NodeScore { NodeId = "old", CombinedScore = 80, StabilityScore = 80, SpeedScore = 80 };
        var close = new NodeScore { NodeId = "close", CombinedScore = 83.9, StabilityScore = 82.9, SpeedScore = 84.9 };
        var clear = new NodeScore { NodeId = "clear", CombinedScore = 84.1, StabilityScore = 83.1, SpeedScore = 85.1 };
        Check(RecommendationSelector.Select("old", [incumbent, close], x => x.CombinedScore, 4)?.NodeId == "old", "综合推荐小幅领先不跳动", failures);
        Check(RecommendationSelector.Select("old", [incumbent, clear], x => x.CombinedScore, 4)?.NodeId == "clear", "综合推荐明显领先才替换", failures);
        Check(RecommendationSelector.Select("old", [clear], x => x.CombinedScore, 4)?.NodeId == "clear", "当前推荐失效立即替换", failures);
        var fastOld = new NodeScore { NodeId = "fast-old", MedianSpeed = 1_000_000 };
        var fastClose = new NodeScore { NodeId = "fast-close", MedianSpeed = 1_090_000 };
        var fastClear = new NodeScore { NodeId = "fast-clear", MedianSpeed = 1_110_000 };
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastOld, fastClose], x => x.MedianSpeed, .10)?.NodeId == "fast-old", "速度推荐小幅领先不跳动", failures);
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastOld, fastClear], x => x.MedianSpeed, .10)?.NodeId == "fast-clear", "速度推荐明显更快才替换", failures);
        Check(RecommendationSelector.SelectByRatio("fast-old", [fastClear], x => x.MedianSpeed, .10)?.NodeId == "fast-clear", "速度推荐失效立即替换", failures);
        foreach (var test in CompatibilityTests.Run())
        {
            Check(test.Passed, $"兼容矩阵：{test.Name} ({test.Detail})", failures);
        }
        foreach (var test in await ClientAdapterTests.RunAsync())
            Check(test.Passed, $"客户端适配：{test.Name} ({test.Detail})", failures);
        var oldConfig = System.Text.Json.Nodes.JsonNode.Parse("""{"indexId":"old"}""")!.AsObject();
        Switcher.UpdateIndexId(oldConfig, "new");
        Check(oldConfig["indexId"]?.GetValue<string>() == "new" && !oldConfig.ContainsKey("IndexId"), "6.x 小写切换键兼容", failures);
        var newConfig = System.Text.Json.Nodes.JsonNode.Parse("""{"IndexId":"old"}""")!.AsObject();
        Switcher.UpdateIndexId(newConfig, "new");
        Check(newConfig["IndexId"]?.GetValue<string>() == "new" && !newConfig.ContainsKey("indexId"), "7.x 大写切换键兼容", failures);

        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var adapters = ClientDiscovery.Discover(settings);
        Check(adapters.Select(x => x.Id).Distinct().Count() == adapters.Count, "适配器 ID 唯一", failures);
        var v2ray = adapters.First(x => x.Id == "v2rayn");
        if (v2ray.IsDetected)
        {
            var nodes = await v2ray.GetNodesAsync();
            Check(nodes.Count > 0, "v2rayN 节点只读识别", failures);
            Check(nodes.All(x => x.Id.StartsWith("v2rayn:", StringComparison.Ordinal)), "跨客户端节点 ID 隔离", failures);
            var compatibility = V2rayNReader.Inspect(settings.V2rayNPath);
            Check(compatibility.CanRead, $"本机 v2rayN {compatibility.Version} 结构兼容", failures);
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
