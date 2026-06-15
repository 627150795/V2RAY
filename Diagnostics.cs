using System.Text.Json;

namespace V2rayNMonitor;

public static class Diagnostics
{
    public static async Task<int> ScoresAsync(string[] args)
    {
        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var engine = new MonitorEngine(settings);
        var terms = args.SkipWhile(x => x != "--scores").Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        var scores = engine.Store.Scores(engine.Nodes(), settings);
        foreach (var score in scores.Where(x => terms.Count == 0 || terms.Any(t => x.Name.Contains(t, StringComparison.OrdinalIgnoreCase))))
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                score.ClientName,
                score.Name,
                score.Subscription,
                score.Samples,
                score.SpeedSamples,
                SuccessRate = score.SuccessText,
                RecentSuccessRate = $"{score.RecentSuccessRate:P0}",
                score.RecentFailures,
                Delay = score.DelayText,
                Speed = score.SpeedText,
                Stability = score.StabilityText,
                SpeedScore = $"{score.SpeedScore:F1}",
                DelayScore = $"{score.DelayScore:F1}",
                Combined = score.CombinedText,
                score.StableEnough,
                score.SpeedEnough,
                score.Status
            }));
        return 0;
    }

    public static async Task<int> RecommendationsAsync()
    {
        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var engine = new MonitorEngine(settings);
        var scores = engine.Store.Scores(engine.Nodes(), settings);
        var stableReady = scores.Where(x => x.StableEnough).ToList();
        var speedReady = scores.Where(x => x.SpeedEnough).ToList();
        var provisionalReady = stableReady.Count > 0 ? stableReady : scores.Where(x => x.Samples > 0).ToList();

        PrintRecommendation("stable", settings.StableRecommendationNodeId, stableReady, x => x.StabilityScore, 3, scores);
        PrintRatioRecommendation("fastest", settings.FastRecommendationNodeId, speedReady, x => x.MedianSpeed, .10, scores);
        PrintRecommendation("combined", settings.BestRecommendationNodeId, speedReady.Count > 0 ? speedReady : provisionalReady,
            x => x.CombinedScore, 4, scores);
        return await Task.FromResult(0);
    }

    private static void PrintRecommendation(string category, string? currentNodeId, IReadOnlyList<NodeScore> candidates,
        Func<NodeScore, double> score, double replacementMargin, IReadOnlyList<NodeScore> allScores)
    {
        var best = candidates.OrderByDescending(score).FirstOrDefault();
        var current = candidates.FirstOrDefault(x => x.NodeId == currentNodeId);
        var selected = RecommendationSelector.Select(currentNodeId, candidates, score, replacementMargin);
        var remembered = allScores.FirstOrDefault(x => x.NodeId == currentNodeId);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Category = category,
            CandidateCount = candidates.Count,
            ReplacementMargin = replacementMargin,
            Selected = Summary(selected, score),
            BestChallenger = Summary(best, score),
            CurrentEligible = current is not null,
            Current = Summary(current ?? remembered, score),
            LeadOverCurrent = best is null || current is null ? (double?)null : score(best) - score(current)
        }));
    }

    private static void PrintRatioRecommendation(string category, string? currentNodeId, IReadOnlyList<NodeScore> candidates,
        Func<NodeScore, double> score, double replacementRatio, IReadOnlyList<NodeScore> allScores)
    {
        var best = candidates.OrderByDescending(score).FirstOrDefault();
        var current = candidates.FirstOrDefault(x => x.NodeId == currentNodeId);
        var selected = RecommendationSelector.SelectByRatio(currentNodeId, candidates, score, replacementRatio);
        var remembered = allScores.FirstOrDefault(x => x.NodeId == currentNodeId);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Category = category,
            CandidateCount = candidates.Count,
            ReplacementRatio = $"{replacementRatio:P0}",
            Selected = Summary(selected, score),
            BestChallenger = Summary(best, score),
            CurrentEligible = current is not null,
            Current = Summary(current ?? remembered, score),
            LeadRatioOverCurrent = best is null || current is null || score(current) <= 0 ? (double?)null : score(best) / score(current) - 1
        }));
    }

    private static object? Summary(NodeScore? score, Func<NodeScore, double> selector) => score is null ? null : new
    {
        score.NodeId,
        score.Name,
        Score = $"{selector(score):F1}",
        score.StableEnough,
        score.SpeedEnough,
        RecentSuccessRate = $"{score.RecentSuccessRate:P0}",
        score.RecentFailures,
        score.Status
    };

    public static async Task<int> ListNodesAsync()
    {
        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var adapter = new V2rayNAdapter(settings);
        foreach (var node in await adapter.GetNodesAsync())
            Console.WriteLine(JsonSerializer.Serialize(new { node.NativeId, node.Name, node.Subscription }));
        return 0;
    }

    public static async Task<int> RunAsync()
    {
        var settings = Paths.LoadSettings();
        settings.V2rayNPath = File.Exists(settings.V2rayNPath) ? settings.V2rayNPath : Paths.LocateV2rayN() ?? "";
        var adapters = ClientDiscovery.Discover(settings);
        foreach (var adapter in adapters)
        {
            List<NodeInfo> nodes = [];
            string? error = null;
            try { nodes = adapter.IsDetected ? await adapter.GetNodesAsync() : []; }
            catch (Exception ex) { error = ex.Message; }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                adapter.Id,
                adapter.Name,
                adapter.IsDetected,
                adapter.SupportsSwitch,
                NodeCount = nodes.Count,
                SubscriptionCount = nodes.Select(x => x.Subscription).Distinct().Count(),
                adapter.Capability.CanRead,
                CanDelay = adapter.Capability.CanDelay,
                adapter.Capability.CanSpeed,
                adapter.Capability.CanSwitch,
                adapter.Capability.Integration,
                adapter.Capability.Detail,
                Error = error
            }));
        }
        return 0;
    }
}
