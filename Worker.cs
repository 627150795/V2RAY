using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ServiceLib.Enums;
using ServiceLib.Handler;
using ServiceLib.Manager;
using ServiceLib.Models.Dto;
using ServiceLib.Models.Entities;

namespace V2rayNMonitor;

public static class Worker
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var sourceExe = Arg(args, "--source") ?? throw new ArgumentException("缺少 --source");
            var kind = Arg(args, "--kind") ?? "delay";
            var limit = int.TryParse(Arg(args, "--limit"), out var n) ? n : 1_048_576;
            var max = int.TryParse(Arg(args, "--max"), out var maxValue) ? maxValue : int.MaxValue;
            var onlyNode = Arg(args, "--node");
            var onlyNodes = (Arg(args, "--nodes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            var timeout = int.TryParse(Arg(args, "--timeout"), out var timeoutValue) ? timeoutValue : 5;
            var root = Path.GetDirectoryName(sourceExe)!;
            V2rayNReader.MakeSnapshot(sourceExe, Path.Combine(Paths.SandboxDir, "guiConfigs"));
            Environment.SetEnvironmentVariable("V2RAYN_MONITOR_ROOT", Paths.SandboxDir);
            Environment.SetEnvironmentVariable("V2RAYN_MONITOR_BIN", Path.Combine(root, "bin"));
            Directory.CreateDirectory(Path.Combine(Paths.SandboxDir, "binConfigs"));
            if (!AppManager.Instance.InitApp()) throw new InvalidOperationException("无法加载 v2rayN 配置快照");
            AppManager.Instance.InitComponents();
            await CoreManager.Instance.Init(AppManager.Instance.Config, (_, _) => Task.CompletedTask);

            var enabledSubs = (await AppManager.Instance.SubItems() ?? []).Where(x => x.Enabled).Select(x => x.Id).ToHashSet();
            var profiles = new List<ProfileItem>();
            foreach (var subId in enabledSubs) profiles.AddRange(await AppManager.Instance.ProfileItems(subId) ?? []);
            profiles = profiles.Where(x => x.IsSub && x.ConfigType != EConfigType.Custom
                && (onlyNode is null || x.IndexId == onlyNode)
                && (onlyNodes.Count == 0 || onlyNodes.Contains(x.IndexId))).Take(max).ToList();
            foreach (var profile in profiles)
            {
                var result = await TestOne(profile, kind, limit, timeout);
                Console.WriteLine(JsonSerializer.Serialize(result));
                Console.Out.Flush();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<TestResult> TestOne(ProfileItem profile, string kind, int limit, int timeout)
    {
        var now = DateTimeOffset.Now.ToUnixTimeSeconds();
        var port = ServiceLib.Common.Utils.GetFreePort(20000);
        var test = new ServerTestItem {
            IndexId=profile.IndexId, Address=profile.Address, Port=port, Profile=profile, ConfigType=profile.ConfigType,
            CoreType=AppManager.Instance.GetCoreType(profile, profile.ConfigType), AllowTest=true
        };
        ServiceLib.Services.ProcessService? process = null;
        try
        {
            process = await CoreManager.Instance.LoadCoreConfigSpeedtest(test);
            if (process is null) return new(profile.IndexId,kind,now,null,null,0,null,"核心启动失败");
            await Task.Delay(800);
            var proxy = new WebProxy($"socks5://127.0.0.1:{test.Port}");
            if (kind == "speed")
            {
                var speed = await SpeedWithFallback(proxy, AppManager.Instance.Config.SpeedTestItem.SpeedTestUrl, limit, timeout);
                var useful = speed >= QualityThresholds.MinUsefulSpeedBytesPerSecond;
                return new(profile.IndexId,kind,now,null,null,useful?1:0,speed,useful?null:"速度测试失败或低于有效阈值");
            }
            var values = new List<double>();
            for (var i=0;i<3;i++)
            {
                var value = await PingWithFallback(proxy, AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl);
                if (value > 0) values.Add(value);
                await Task.Delay(150);
            }
            values.Sort();
            var median = values.Count==0 ? (double?)null : values[values.Count/2];
            var jitter = values.Count<2 ? 0 : values.Max()-values.Min();
            return new(profile.IndexId,kind,now,median,jitter,values.Count/3d,null,values.Count>0?null:"延迟测试失败");
        }
        catch (Exception ex) { return new(profile.IndexId,kind,now,null,null,0,null,ex.Message); }
        finally { if (process is not null) await process.StopAsync(); }
    }

    private static async Task<double> Ping(IWebProxy proxy, string url)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new HttpClient(new SocketsHttpHandler { Proxy=proxy, UseProxy=true }) { Timeout=Timeout.InfiniteTimeSpan };
        var sw=Stopwatch.StartNew(); using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cts.Token);
        response.EnsureSuccessStatusCode(); sw.Stop(); return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<double> PingWithFallback(IWebProxy proxy, string url)
    {
        try { return await Ping(proxy, url); }
        catch when (!url.Equals("http://www.gstatic.com/generate_204", StringComparison.OrdinalIgnoreCase))
        {
            return await Ping(proxy, "http://www.gstatic.com/generate_204");
        }
    }

    private static async Task<double> Speed(IWebProxy proxy, string url, int limit, int timeout)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        using var client = new HttpClient(new SocketsHttpHandler { Proxy=proxy, UseProxy=true }) { Timeout=Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get,url); request.Headers.Range=new(0,limit-1);
        var sw=Stopwatch.StartNew(); var total=0;
        try
        {
            using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cts.Token);
            response.EnsureSuccessStatusCode(); await using var stream=await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer=new byte[32*1024];
            while(total<limit) { var read=await stream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,limit-total)),cts.Token); if(read==0)break; total+=read; }
        }
        catch (OperationCanceledException) when (total > 0) { }
        sw.Stop(); return sw.Elapsed.TotalSeconds>0?total/sw.Elapsed.TotalSeconds:0;
    }

    private static async Task<double> SpeedWithFallback(IWebProxy proxy, string configuredUrl, int limit, int timeout)
    {
        var urls = new[]
        {
            configuredUrl,
            "https://cachefly.cachefly.net/1mb.test",
            $"https://speed.cloudflare.com/__down?bytes={Math.Max(262144, limit)}"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        var best = 0d;
        foreach (var url in urls)
        {
            try
            {
                var speed = await Speed(proxy, url, limit, timeout);
                best = Math.Max(best, speed);
                if (speed >= QualityThresholds.MinUsefulSpeedBytesPerSecond) return speed;
            }
            catch
            {
            }
        }
        return best;
    }
    private static string? Arg(string[] args,string key) { var i=Array.IndexOf(args,key); return i>=0&&i+1<args.Length?args[i+1]:null; }
}
