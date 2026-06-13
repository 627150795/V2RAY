using System.Diagnostics;
using System.Text.Json;

namespace V2rayNMonitor;

public sealed class MonitorEngine
{
    public MonitorSettings Settings { get; }
    public HistoryStore Store { get; } = new();
    public IReadOnlyList<IClientAdapter> Adapters { get; }
    public bool IsRunning { get; private set; }
    private Process? _worker;
    private CancellationTokenSource? _cts;
    public event Action<string>? Status;
    public event Action? Updated;

    public MonitorEngine(MonitorSettings settings)
    {
        Settings = settings;
        Adapters = ClientDiscovery.Discover(settings);
    }

    public List<NodeInfo> Nodes() => Adapters.Where(x => x.IsDetected)
        .SelectMany(x => x.GetNodesAsync().GetAwaiter().GetResult()).ToList();

    public async Task RunAsync(string kind)
    {
        if (IsRunning || Settings.Paused) return;
        IsRunning = true;
        _cts = new();
        Status?.Invoke(kind == "speed" ? "正在执行轻量速度测试" : "正在测试真实延迟");
        try
        {
            var allNodes = Nodes();
            var results = new List<TestResult>();
            var v2Nodes = allNodes.Where(x => x.ClientId == "v2rayn").ToList();
            var v2Compatibility = File.Exists(Settings.V2rayNPath) ? V2rayNReader.Inspect(Settings.V2rayNPath) : null;
            if (v2Nodes.Count > 0 && v2Compatibility?.CanTest == true)
            {
                var raw = await RunV2rayNWorker(kind, Settings.SpeedLimitBytes, null);
                results.AddRange(MapV2rayN(raw, v2Nodes));
                if (kind == "speed" && raw.Count > 0)
                {
                    var topNativeIds = raw.Where(x => x.SpeedBytesPerSecond >= QualityThresholds.MinUsefulSpeedBytesPerSecond)
                        .OrderByDescending(x => x.SpeedBytesPerSecond).Take(Settings.RefineTopCount)
                        .Select(x => x.NodeId).ToList();
                    if (topNativeIds.Count > 0)
                    {
                        var refined = MapV2rayN(await RunV2rayNWorker(kind, Settings.RefineSpeedLimitBytes, topNativeIds), v2Nodes);
                        var refinedIds = refined.Select(x => x.NodeId).ToHashSet();
                        results = results.Where(x => !refinedIds.Contains(x.NodeId)).Concat(refined).ToList();
                    }
                }
            }
            else if (v2Nodes.Count > 0 && kind is "delay" or "speed")
            {
                Status?.Invoke($"v2rayN {v2Compatibility?.Version ?? "未知版本"} 可读取，但当前版本未启用隔离核心测试。");
            }

            if (kind == "delay")
            {
                foreach (var adapter in Adapters.Where(x => x.IsDetected && x.Id != "v2rayn" && x.Capability.CanDelay))
                {
                    var nodes = allNodes.Where(x => x.ClientId == adapter.Id).ToList();
                    results.AddRange(await adapter.RunDelayAsync(nodes, _cts.Token));
                }
            }

            if (results.Count > 0) Store.Add(results);
            if (kind == "speed") Settings.LastSpeedRun = DateTime.Now; else Settings.LastDelayRun = DateTime.Now;
            Paths.SaveSettings(Settings);
            Store.Cleanup();
            Status?.Invoke($"完成：记录 {results.Count} 个节点的{(kind == "speed" ? "速度" : "延迟")}结果");
        }
        catch (OperationCanceledException) { Status?.Invoke("当前测试已停止"); }
        catch (Exception ex) { Status?.Invoke($"测试失败：{ex.Message}"); }
        finally { _worker = null; _cts?.Dispose(); _cts = null; IsRunning = false; Updated?.Invoke(); }
    }

    public async Task SwitchAsync(NodeInfo node)
    {
        var adapter = Adapters.First(x => x.Id == node.ClientId);
        if (!adapter.SupportsSwitch) throw new NotSupportedException($"{adapter.Name} 当前仅监控，尚未启用安全切换。");
        Cancel();
        await adapter.SwitchAsync(node);
    }

    public async Task<TestResult> ValidateSwitchTargetAsync(NodeInfo node)
    {
        if (IsRunning) throw new InvalidOperationException("请先等待当前测试完成，或停止当前测试后再切换。");
        IsRunning = true;
        _cts = new();
        Status?.Invoke($"切换前复测：{node.Name}");
        try
        {
            List<TestResult> results;
            if (node.ClientId == "v2rayn")
            {
                results = MapV2rayN(await RunV2rayNWorker("delay", Settings.SpeedLimitBytes, [node.NativeId]), [node]).ToList();
            }
            else
            {
                var adapter = Adapters.First(x => x.Id == node.ClientId);
                if (!adapter.Capability.CanDelay) throw new NotSupportedException($"{adapter.Name} 无法执行切换前实时复测。");
                results = (await adapter.RunDelayAsync([node], _cts.Token)).ToList();
            }

            var result = results.FirstOrDefault() ?? throw new InvalidOperationException("切换前复测没有返回结果。");
            Store.Add([result]);
            Updated?.Invoke();
            return result;
        }
        finally
        {
            _worker = null;
            _cts?.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private async Task<List<TestResult>> RunV2rayNWorker(string kind, int limit, List<string>? nativeIds)
    {
        var nodeArg = nativeIds is { Count: > 0 } ? $" --nodes {string.Join(',', nativeIds)}" : "";
        var psi = new ProcessStartInfo(Environment.ProcessPath!,
            $"--worker --source \"{Settings.V2rayNPath}\" --kind {kind} --limit {limit} --timeout {Settings.SpeedTimeoutSeconds}{nodeArg}")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        _worker = p;
        var results = new List<TestResult>();
        while (await p.StandardOutput.ReadLineAsync() is { } line)
        {
            try { var x = JsonSerializer.Deserialize<TestResult>(line); if (x is not null) results.Add(x); } catch { }
        }
        await p.WaitForExitAsync(_cts?.Token ?? default);
        return results;
    }

    private static IEnumerable<TestResult> MapV2rayN(IEnumerable<TestResult> results, IReadOnlyList<NodeInfo> nodes)
    {
        var ids = nodes.ToDictionary(x => x.NativeId, x => x.Id);
        foreach (var result in results)
            if (ids.TryGetValue(result.NodeId, out var id)) yield return result with { NodeId = id };
    }

    public void Cancel()
    {
        _cts?.Cancel();
        try { if (_worker is { HasExited: false }) _worker.Kill(true); } catch { }
    }
}
