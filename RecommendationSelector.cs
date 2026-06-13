namespace V2rayNMonitor;

public static class RecommendationSelector
{
    public static NodeScore? Select(string? currentNodeId, IReadOnlyList<NodeScore> candidates,
        Func<NodeScore, double> score, double replacementMargin)
    {
        var best = candidates.OrderByDescending(score).FirstOrDefault();
        if (best is null) return null;

        var current = candidates.FirstOrDefault(x => x.NodeId == currentNodeId);
        if (current is null) return best;
        return score(best) - score(current) >= replacementMargin ? best : current;
    }
}
