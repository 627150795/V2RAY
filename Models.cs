namespace V2rayNMonitor;

public static class QualityThresholds
{
    public const int RequiredDelaySamples = 24;
    public const int RequiredSpeedSamples = 3;
    public const int RecentDelaySamples = 12;
    public const double RequiredSuccessRate = .90;
    public const double MinUsefulSpeedBytesPerSecond = 16 * 1024;
}

public sealed class MonitorSettings
{
    public string V2rayNPath { get; set; } = "";
    public bool EnableV2rayN { get; set; } = true;
    public bool EnableMihomo { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public int DelayIntervalMinutes { get; set; } = 30;
    public bool EnableIdleDelayProbe { get; set; } = true;
    public int IdleDelayIntervalMinutes { get; set; } = 10;
    public int IdleRequiredMinutes { get; set; } = 3;
    public int IdleNetworkThresholdKBps { get; set; } = 128;
    public string DailySpeedTime { get; set; } = "03:00";
    public int SpeedLimitBytes { get; set; } = 2_097_152;
    public int RefineSpeedLimitBytes { get; set; } = 10_485_760;
    public int RefineTopCount { get; set; } = 10;
    public int SpeedTimeoutSeconds { get; set; } = 5;
    public int HistoryDays { get; set; } = 7;
    public double StabilityWeight { get; set; } = .45;
    public double SpeedWeight { get; set; } = .30;
    public double DelayWeight { get; set; } = .25;
    public bool Paused { get; set; }
    public DateTime? LastDelayRun { get; set; }
    public DateTime? LastIdleDelayRun { get; set; }
    public DateTime? LastSpeedRun { get; set; }
    public string? StableRecommendationNodeId { get; set; }
    public string? FastRecommendationNodeId { get; set; }
    public string? BestRecommendationNodeId { get; set; }
}

public sealed record NodeInfo(string Id, string NativeId, string ClientId, string ClientName, string Name,
    string Subscription, int ConfigType, string Address, int Port);
public sealed record TestResult(string NodeId, string Kind, long Timestamp, double? DelayMs, double? JitterMs,
    double? SuccessRate, double? SpeedBytesPerSecond, string? Error);

public sealed class NodeScore
{
    public string NodeId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string ClientName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Subscription { get; init; } = "";
    public int Samples { get; init; }
    public int SpeedSamples { get; init; }
    public double SuccessRate { get; init; }
    public double MedianDelay { get; init; }
    public double P95Delay { get; init; }
    public double Jitter { get; init; }
    public double MedianSpeed { get; init; }
    public int RecentFailures { get; init; }
    public double RecentSuccessRate { get; init; }
    public double RecentMedianDelay { get; init; }
    public double RecentP95Delay { get; init; }
    public double StabilityScore { get; set; }
    public double SpeedScore { get; set; }
    public double DelayScore { get; set; }
    public double CombinedScore { get; set; }
    public double Confidence { get; set; }
    public bool StableEnough => Samples >= QualityThresholds.RequiredDelaySamples
        && SuccessRate >= QualityThresholds.RequiredSuccessRate
        && RecentSuccessRate >= QualityThresholds.RequiredSuccessRate
        && RecentFailures < 2;
    public bool SpeedEnough => StableEnough && SpeedSamples >= QualityThresholds.RequiredSpeedSamples && MedianSpeed >= QualityThresholds.MinUsefulSpeedBytesPerSecond;
    public bool DataEnough => SpeedEnough;
    public string Status => StatusText();
    public string DelayText => MedianDelay > 0 ? $"{MedianDelay:F0} ms" : "-";
    public string SpeedText => SpeedSamples == 0 || MedianSpeed < QualityThresholds.MinUsefulSpeedBytesPerSecond ? "无有效测速" :
        MedianSpeed < 1024 * 1024 ? $"{MedianSpeed / 1024:F0} KiB/s" : $"{MedianSpeed / 1024 / 1024:F2} MiB/s";
    public string SuccessText => $"{SuccessRate:P1}";
    public string CombinedText => $"{CombinedScore:F1}";
    public string StabilityText => $"{StabilityScore:F1}";
    public string SpeedScoreText => $"{SpeedScore:F1}";
    public string DelayScoreText => $"{DelayScore:F1}";
    public string SampleText => $"{Samples} 延迟 / {SpeedSamples} 速度";

    private string StatusText()
    {
        if (DataEnough) return "充足";
        var missingDelay = Math.Max(0, QualityThresholds.RequiredDelaySamples - Samples);
        var missingSpeed = Math.Max(0, QualityThresholds.RequiredSpeedSamples - SpeedSamples);
        if (missingDelay > 0 && missingSpeed > 0) return $"还差 {missingDelay} 延迟 / {missingSpeed} 测速";
        if (missingDelay > 0) return $"还差 {missingDelay} 次延迟";
        if (missingSpeed > 0) return $"还差 {missingSpeed} 次有效测速";
        if (MedianSpeed < QualityThresholds.MinUsefulSpeedBytesPerSecond) return "测速低于有效阈值";
        if (RecentSuccessRate < QualityThresholds.RequiredSuccessRate) return $"近期成功率需到 {QualityThresholds.RequiredSuccessRate:P0}";
        if (SuccessRate < QualityThresholds.RequiredSuccessRate) return $"成功率需到 {QualityThresholds.RequiredSuccessRate:P0}";
        if (RecentFailures >= 2) return $"连续失败 {RecentFailures} 次";
        return "继续观察";
    }
}

public sealed class SubscriptionScore
{
    public string Name { get; init; } = "";
    public int NodeCount { get; init; }
    public int AvailableCount { get; init; }
    public double AverageDelay { get; init; }
    public string BestNode { get; init; } = "-";
    public double Availability => NodeCount == 0 ? 0 : (double)AvailableCount / NodeCount;
}
