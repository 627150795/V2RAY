using Microsoft.Data.Sqlite;

namespace V2rayNMonitor;

public sealed class HistoryStore
{
    public HistoryStore()
    {
        Directory.CreateDirectory(Paths.DataDir);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS results(
              id INTEGER PRIMARY KEY, node_id TEXT NOT NULL, kind TEXT NOT NULL, ts INTEGER NOT NULL,
              delay_ms REAL, jitter_ms REAL, success_rate REAL, speed_bps REAL, error TEXT);
            CREATE INDEX IF NOT EXISTS ix_results_node_ts ON results(node_id, ts);
            """;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Open()
    {
        var db = new SqliteConnection($"Data Source={Paths.HistoryFile}");
        db.Open();
        return db;
    }

    public void Add(IEnumerable<TestResult> results)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var x in results)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO results(node_id,kind,ts,delay_ms,jitter_ms,success_rate,speed_bps,error) VALUES($n,$k,$t,$d,$j,$r,$s,$e)";
            cmd.Parameters.AddWithValue("$n", x.NodeId); cmd.Parameters.AddWithValue("$k", x.Kind); cmd.Parameters.AddWithValue("$t", x.Timestamp);
            cmd.Parameters.AddWithValue("$d", (object?)x.DelayMs ?? DBNull.Value); cmd.Parameters.AddWithValue("$j", (object?)x.JitterMs ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$r", (object?)x.SuccessRate ?? DBNull.Value); cmd.Parameters.AddWithValue("$s", (object?)x.SpeedBytesPerSecond ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$e", (object?)x.Error ?? DBNull.Value); cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public List<NodeScore> Scores(IReadOnlyList<NodeInfo> nodes, MonitorSettings settings)
    {
        var since = DateTimeOffset.Now.AddDays(-settings.HistoryDays).ToUnixTimeSeconds();
        using var db = Open();
        var scores = new List<NodeScore>();
        foreach (var node in nodes)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT kind,delay_ms,jitter_ms,success_rate,speed_bps FROM results WHERE node_id=$n AND ts>=$t ORDER BY ts";
            cmd.Parameters.AddWithValue("$n", node.Id); cmd.Parameters.AddWithValue("$t", since);
            using var reader = cmd.ExecuteReader();
            var delays = new List<double>(); var jitters = new List<double>(); var delayRates = new List<double>();
            var observations = new List<(double? Delay, double Jitter, double Rate)>();
            var speeds = new List<double>(); var recentDelayRates = new List<double>();
            while (reader.Read())
            {
                var kind = reader.GetString(0);
                if (kind == "delay")
                {
                    var delay = !reader.IsDBNull(1) && reader.GetDouble(1) > 0 ? reader.GetDouble(1) : (double?)null;
                    var jitter = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);
                    var rate = reader.IsDBNull(3) ? 0 : reader.GetDouble(3);
                    if (delay is not null) delays.Add(delay.Value);
                    jitters.Add(jitter); delayRates.Add(rate); recentDelayRates.Add(rate);
                    observations.Add((delay, jitter, rate));
                }
                else if (kind == "speed" && !reader.IsDBNull(4) && reader.GetDouble(4) >= QualityThresholds.MinUsefulSpeedBytesPerSecond) speeds.Add(reader.GetDouble(4));
            }
            delays.Sort(); speeds.Sort();
            var recent = observations.TakeLast(QualityThresholds.RecentDelaySamples).ToList();
            var recentDelays = recent.Where(x => x.Delay is not null).Select(x => x.Delay!.Value).Order().ToList();
            scores.Add(new()
            {
                NodeId = node.Id, ClientId = node.ClientId, ClientName = node.ClientName, Name = node.Name,
                Subscription = node.Subscription, Samples = delayRates.Count, SpeedSamples = speeds.Count,
                SuccessRate = delayRates.Count == 0 ? 0 : delayRates.Average(), MedianDelay = Median(delays),
                P95Delay = Percentile(delays, .95), Jitter = jitters.Count == 0 ? 0 : Median(jitters.Order().ToList()),
                MedianSpeed = Median(speeds), RecentFailures = ConsecutiveFailures(recentDelayRates),
                RecentSuccessRate = recent.Count == 0 ? 0 : recent.Average(x => x.Rate),
                RecentMedianDelay = Median(recentDelays), RecentP95Delay = Percentile(recentDelays, .95)
            });
        }

        Rank(scores.Where(x => x.Samples > 0).ToList(), x => AbsoluteStability(x), (x, v) => x.StabilityScore = .80 * AbsoluteStability(x) + .20 * v);
        Rank(scores.Where(x => x.MedianSpeed > 0).ToList(), x => x.MedianSpeed, (x, v) => x.SpeedScore = .80 * AbsoluteSpeed(x.MedianSpeed) + .20 * v);
        Rank(scores.Where(x => x.MedianDelay > 0).ToList(), x => -EffectiveDelay(x),
            (x, v) => x.DelayScore = .75 * AbsoluteDelay(EffectiveDelay(x)) + .25 * v);
        foreach (var x in scores)
        {
            var delayConfidence = Math.Min(1, x.Samples / (double)QualityThresholds.RequiredDelaySamples);
            var speedConfidence = Math.Min(1, x.SpeedSamples / (double)QualityThresholds.RequiredSpeedSamples);
            x.Confidence = delayConfidence * (.65 + .35 * speedConfidence);
            var effectiveSpeed = x.SpeedScore * (.35 + .65 * speedConfidence);
            var raw = x.StabilityScore * settings.StabilityWeight + effectiveSpeed * settings.SpeedWeight + x.DelayScore * settings.DelayWeight;
            x.CombinedScore = raw * (.70 + .30 * x.Confidence) * RecentPenalty(x);
        }
        return scores.OrderByDescending(x => x.CombinedScore).ToList();
    }

    public void Cleanup()
    {
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM results WHERE ts<$t";
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.Now.AddDays(-30).ToUnixTimeSeconds()); cmd.ExecuteNonQuery();
    }

    private static double AbsoluteStability(NodeScore x)
    {
        if (x.Samples == 0) return 0;
        var availability = x.SuccessRate * 100;
        var recentAvailability = x.RecentSuccessRate * 100;
        var jitter = 100 * Math.Exp(-x.Jitter / 120);
        var recentP95 = 100 * Math.Exp(-(x.RecentP95Delay > 0 ? x.RecentP95Delay : x.P95Delay) / 800);
        var baseScore = .40 * availability + .30 * recentAvailability + .15 * jitter + .15 * recentP95;
        var failurePenalty = x.RecentFailures switch { >= 3 => .35, 2 => .55, 1 => .82, _ => 1d };
        return baseScore * failurePenalty;
    }
    private static double EffectiveDelay(NodeScore x) => x.RecentMedianDelay > 0 ? .35 * x.MedianDelay + .65 * x.RecentMedianDelay : x.MedianDelay;
    private static double AbsoluteDelay(double value) => value <= 0 ? 0 : 100 * Math.Exp(-value / 500);
    private static double AbsoluteSpeed(double value) => value <= 0 ? 0 : Math.Min(100, 100 * Math.Log10(1 + value / 262144) / Math.Log10(1 + 20 * 1024 * 1024 / 262144));
    private static int ConsecutiveFailures(List<double> rates)
    {
        var count = 0;
        for (var i = rates.Count - 1; i >= 0 && rates[i] <= 0; i--) count++;
        return count;
    }
    private static double RecentPenalty(NodeScore x)
    {
        var penalty = x.RecentSuccessRate switch { < .80 => .45, < .90 => .70, _ => 1d };
        if (x.MedianDelay > 0 && x.RecentMedianDelay > Math.Max(x.MedianDelay * 2, x.MedianDelay + 200)) penalty *= .55;
        else if (x.MedianDelay > 0 && x.RecentMedianDelay > Math.Max(x.MedianDelay * 1.5, x.MedianDelay + 100)) penalty *= .78;
        if (x.RecentFailures >= 2) penalty *= .55;
        return penalty;
    }
    internal static double MedianForTest(List<double> x) => Median(x);

    private static double Median(List<double> x)
    {
        if (x.Count == 0) return 0;
        var mid = x.Count / 2;
        return x.Count % 2 == 1 ? x[mid] : (x[mid - 1] + x[mid]) / 2;
    }
    private static double Percentile(List<double> x, double p) => x.Count == 0 ? 0 : x[(int)Math.Clamp(Math.Ceiling(p * x.Count) - 1, 0, x.Count - 1)];
    private static void Rank(List<NodeScore> xs, Func<NodeScore, double> value, Action<NodeScore, double> set)
    {
        var ordered = xs.OrderBy(value).ToList();
        for (var i = 0; i < ordered.Count;)
        {
            var current = value(ordered[i]);
            var end = i;
            while (end + 1 < ordered.Count && Math.Abs(value(ordered[end + 1]) - current) < .000001) end++;
            var percentile = ordered.Count <= 1 ? 100 : 100d * end / (ordered.Count - 1);
            for (var j = i; j <= end; j++) set(ordered[j], percentile);
            i = end + 1;
        }
    }
}
