using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Pipes;
using System.Diagnostics;
using YamlDotNet.Serialization;

namespace V2rayNMonitor;

public sealed record ClientCapability(
    string Id, string Name, bool Detected, bool CanRead, bool CanDelay, bool CanSpeed, bool CanSwitch,
    string Integration, string Detail);

public interface IClientAdapter
{
    string Id { get; }
    string Name { get; }
    bool IsDetected { get; }
    bool SupportsSwitch { get; }
    ClientCapability Capability { get; }
    Task<List<NodeInfo>> GetNodesAsync();
    Task<List<TestResult>> RunDelayAsync(IReadOnlyList<NodeInfo> nodes, CancellationToken cancellationToken);
    Task SwitchAsync(NodeInfo node);
}

public static class ClientDiscovery
{
    public static IReadOnlyList<IClientAdapter> Discover(MonitorSettings settings)
    {
        var result = new List<IClientAdapter> { new V2rayNAdapter(settings) };
        result.AddRange(MihomoAdapter.Discover());
        result.AddRange(SingBoxConfigAdapter.Discover());
        result.AddRange(PortableAdapters());
        return result.DistinctBy(x => x.Id).ToList();
    }

    private static IEnumerable<IClientAdapter> PortableAdapters()
    {
        foreach (var process in Process.GetProcesses())
        {
            string name; string? root;
            try { name = process.ProcessName; root = Path.GetDirectoryName(process.MainModule?.FileName); } catch { continue; }
            if (root is null || !Directory.Exists(root)) continue;
            if (root.Contains("v2rayN", StringComparison.OrdinalIgnoreCase)) continue;
            var key = name.ToLowerInvariant();
            if (key.Contains("hiddify")) yield return new SingBoxConfigAdapter(new("hiddify-portable", "Hiddify（便携）", root));
            else if (key.Contains("nekobox") || key.Contains("nekoray")) yield return new SingBoxConfigAdapter(new("nekobox-portable", "NekoBox / NekoRay（便携）", root));
            else if (key.Contains("sing-box") || key.Contains("singbox")) yield return new SingBoxConfigAdapter(new("sing-box-portable", "sing-box（便携）", root));
            else if (key.Contains("flclash")) yield return new MihomoAdapter(new("flclash-portable", "FlClash（便携）", root, ["config.yaml", "clash.yaml"]));
        }
    }
}

public sealed class V2rayNAdapter : IClientAdapter
{
    private readonly MonitorSettings _settings;
    public V2rayNAdapter(MonitorSettings settings) => _settings = settings;
    public string Id => "v2rayn";
    public string Name => "v2rayN";
    public bool IsDetected => _settings.EnableV2rayN && File.Exists(_settings.V2rayNPath);
    public bool SupportsSwitch => Capability.CanSwitch;
    public ClientCapability Capability
    {
        get
        {
            if (!IsDetected) return new(Id, Name, false, false, false, false, false, "v2rayN SQLite + 本机核心", "未检测到");
            var c = V2rayNReader.Inspect(_settings.V2rayNPath);
            return new(Id, Name, true, c.CanRead, c.CanTest, c.CanTest, c.CanSwitch, "v2rayN SQLite + 本机核心", $"{c.Version}；{c.Summary}");
        }
    }

    public Task<List<NodeInfo>> GetNodesAsync()
    {
        if (!IsDetected) return Task.FromResult(new List<NodeInfo>());
        var compatibility = V2rayNReader.Inspect(_settings.V2rayNPath);
        if (!compatibility.CanRead) throw new InvalidDataException(compatibility.Summary);
        var db = V2rayNReader.MakeSnapshot(_settings.V2rayNPath, Path.Combine(Paths.DataDir, $"read-snapshot-{Environment.ProcessId}"));
        return Task.FromResult(V2rayNReader.ReadEnabledNodes(db));
    }
    public Task<List<TestResult>> RunDelayAsync(IReadOnlyList<NodeInfo> nodes, CancellationToken cancellationToken) => Task.FromResult(new List<TestResult>());
    public Task SwitchAsync(NodeInfo node) => Switcher.SwitchAsync(_settings.V2rayNPath, node.NativeId);
}

public sealed record MihomoDefinition(string Id, string Name, string Directory, string[] ConfigNames);

public sealed class MihomoAdapter : IClientAdapter
{
    private readonly MihomoDefinition _definition;
    private readonly string? _configPath;
    private readonly string? _controller;
    private readonly string? _controllerPipe;
    private readonly string? _secret;
    private readonly bool _controllerOnline;

    public MihomoAdapter(MihomoDefinition definition)
    {
        _definition = definition;
        var configs = definition.ConfigNames.Select(x => Path.Combine(definition.Directory, x)).Where(File.Exists).ToList();
        _configPath = configs.OrderByDescending(ProxyCount).FirstOrDefault();
        if (_configPath is null) return;
        foreach (var path in configs)
        {
            var root = ReadYaml(path);
            _controller ??= EmptyToNull(Scalar(root, "external-controller"));
            _controllerPipe ??= EmptyToNull(Scalar(root, "external-controller-pipe"));
            _secret ??= EmptyToNull(Scalar(root, "secret"));
        }
        _controllerOnline = ProbeController();
    }

    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public bool IsDetected => Directory.Exists(_definition.Directory);
    public bool SupportsSwitch => false;
    public ClientCapability Capability => new(Id, Name, IsDetected, _configPath is not null, _controllerOnline, false, false,
        _controllerOnline ? (_controllerPipe is null ? "Mihomo REST API" : "Mihomo 命名管道 API") : "Mihomo YAML 只读",
        _configPath is null ? "已检测到目录，但未找到配置" : _controllerOnline ? $"控制接口在线：{_controller ?? _controllerPipe}" : "控制接口离线；从配置读取节点");

    public static IEnumerable<IClientAdapter> Discover()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var definitions = new[]
        {
            new MihomoDefinition("clash-verge-rev", "Clash Verge Rev", Path.Combine(roaming, "io.github.clash-verge-rev.clash-verge-rev"), ["config.yaml","clash-verge.yaml"]),
            new MihomoDefinition("mihomo-party", "Mihomo Party", Path.Combine(roaming, "mihomo-party"), ["config.yaml","mihomo.yaml"]),
            new MihomoDefinition("clash-nyanpasu", "Clash Nyanpasu", Path.Combine(roaming, "clash-nyanpasu"), ["config.yaml","clash.yaml"]),
            new MihomoDefinition("flclash", "FlClash", Path.Combine(roaming, "FlClash"), ["config.yaml","clash.yaml"]),
            new MihomoDefinition("gui-for-clash", "GUI.for.Clash", Path.Combine(roaming, "GUI.for.Clash"), ["config.yaml","clash.yaml"]),
            new MihomoDefinition("clash-meta", "Clash Meta / Mihomo", Path.Combine(local, "mihomo"), ["config.yaml"]),
            new MihomoDefinition("clash-config", "Clash 配置目录", Path.Combine(home, ".config", "clash"), ["config.yaml"]),
            new MihomoDefinition("mihomo-config", "Mihomo 配置目录", Path.Combine(home, ".config", "mihomo"), ["config.yaml"])
        };
        return definitions.Where(x => Directory.Exists(x.Directory)).Select(x => new MihomoAdapter(x));
    }

    public async Task<List<NodeInfo>> GetNodesAsync()
    {
        if (_controllerOnline) return await GetApiNodes();
        return GetYamlNodes();
    }

    public async Task<List<TestResult>> RunDelayAsync(IReadOnlyList<NodeInfo> nodes, CancellationToken cancellationToken)
    {
        if (!_controllerOnline || _controller is null) return [];
        using var client = Client();
        var results = new List<TestResult>();
        foreach (var node in nodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.Now.ToUnixTimeSeconds();
            try
            {
                var url = Uri.EscapeDataString("https://www.gstatic.com/generate_204");
                var name = Uri.EscapeDataString(node.Name);
                using var json = JsonDocument.Parse(await client.GetStringAsync(ApiUrl($"/proxies/{name}/delay?timeout=5000&url={url}"), cancellationToken));
                var delay = json.RootElement.GetProperty("delay").GetDouble();
                results.Add(new(node.Id, "delay", now, delay, 0, 1, null, null));
            }
            catch (Exception ex) { results.Add(new(node.Id, "delay", now, null, null, 0, null, ex.Message)); }
        }
        return results;
    }

    public Task SwitchAsync(NodeInfo node) => throw new NotSupportedException($"{Name} 当前仅启用安全监控，未自动猜测目标代理组。");

    private async Task<List<NodeInfo>> GetApiNodes()
    {
        if (_controller is null && _controllerPipe is null) return [];
        using var client = Client();
        try
        {
            using var json = JsonDocument.Parse(await client.GetStringAsync(ApiUrl("/providers/proxies")));
            var result = new List<NodeInfo>();
            foreach (var provider in json.RootElement.GetProperty("providers").EnumerateObject())
            {
                if (!provider.Value.TryGetProperty("proxies", out var proxies)) continue;
                foreach (var proxy in proxies.EnumerateArray())
                {
                    var name = proxy.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var type = proxy.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    Add(result, name, provider.Name, type, "", 0);
                }
            }
            return result.DistinctBy(x => x.Id).ToList();
        }
        catch { return GetYamlNodes(); }
    }

    private List<NodeInfo> GetYamlNodes()
    {
        if (_configPath is null) return [];
        var result = new List<NodeInfo>();
        AddYamlNodes(result, _configPath, "当前合并配置");
        var profiles = Path.Combine(_definition.Directory, "profiles");
        if (Directory.Exists(profiles))
            foreach (var path in Directory.GetFiles(profiles, "*.yaml").Where(x => new FileInfo(x).Length < 10_000_000).Take(100))
                AddYamlNodes(result, path, Path.GetFileNameWithoutExtension(path));
        return result.DistinctBy(x => $"{x.Name}\n{x.Address}\n{x.Port}").ToList();
    }

    private void AddYamlNodes(List<NodeInfo> result, string path, string subscription)
    {
        var root = ReadYaml(path);
        if (!root.TryGetValue("proxies", out var value) || value is not List<object> proxies) return;
        foreach (var item in proxies.OfType<Dictionary<object, object>>())
        {
            var name = Scalar(item, "name") ?? "";
            var type = Scalar(item, "type") ?? "";
            var server = Scalar(item, "server") ?? "";
            _ = int.TryParse(Scalar(item, "port"), out var port);
            Add(result, name, subscription, type, server, port);
        }
    }

    private void Add(List<NodeInfo> result, string name, string subscription, string type, string address, int port)
    {
        if (string.IsNullOrWhiteSpace(name) || IsGroup(type)) return;
        var native = $"{subscription}\n{name}";
        result.Add(new(Key(Id, native), native, Id, Name, name, subscription, 0, address.Length > 0 ? address : type, port));
    }
    private bool ProbeController()
    {
        if (string.IsNullOrWhiteSpace(_controller) && string.IsNullOrWhiteSpace(_controllerPipe)) return false;
        try { using var client = Client(); return client.GetAsync(ApiUrl("/version")).GetAwaiter().GetResult().IsSuccessStatusCode; }
        catch { return false; }
    }
    private HttpClient Client()
    {
        HttpClient client;
        if (!string.IsNullOrWhiteSpace(_controllerPipe))
        {
            var pipeName = _controllerPipe.Replace(@"\\.\pipe\", "", StringComparison.OrdinalIgnoreCase);
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(cancellationToken);
                    return pipe;
                }
            };
            client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };
        }
        else client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        if (!string.IsNullOrEmpty(_secret)) client.DefaultRequestHeaders.Authorization = new("Bearer", _secret);
        return client;
    }
    private string ApiUrl(string path) => _controllerPipe is not null ? $"http://localhost{path}" : $"http://{_controller}{path}";
    private static Dictionary<object, object> ReadYaml(string path)
    {
        try { return new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path)) ?? []; }
        catch { return []; }
    }
    private static string? Scalar(Dictionary<object, object> map, string key) =>
        map.FirstOrDefault(x => string.Equals(Convert.ToString(x.Key), key, StringComparison.OrdinalIgnoreCase)).Value?.ToString();
    private static int ProxyCount(string path)
    {
        var root = ReadYaml(path);
        return root.TryGetValue("proxies", out var value) && value is System.Collections.ICollection collection ? collection.Count : 0;
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static bool IsGroup(string type) => type is "Selector" or "URLTest" or "Fallback" or "LoadBalance" or "Relay" or "Compatible" or "DIRECT" or "REJECT";
    private static string Key(string client, string value) => client + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}

public sealed record SingBoxDefinition(string Id, string Name, string Directory);

public sealed class SingBoxConfigAdapter : IClientAdapter
{
    private readonly SingBoxDefinition _definition;
    private readonly List<string> _configs;
    public SingBoxConfigAdapter(SingBoxDefinition definition)
    {
        _definition = definition;
        _configs = FindConfigs(definition.Directory);
    }
    public string Id => _definition.Id;
    public string Name => _definition.Name;
    public bool IsDetected => Directory.Exists(_definition.Directory);
    public bool SupportsSwitch => false;
    public ClientCapability Capability => new(Id, Name, IsDetected, _configs.Count > 0, false, false, false,
        "sing-box JSON 只读", _configs.Count > 0 ? $"发现 {_configs.Count} 个可解析配置" : "已检测到目录，但未找到可解析配置");

    public static IEnumerable<IClientAdapter> Discover()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var definitions = new[]
        {
            new SingBoxDefinition("hiddify", "Hiddify", FirstExisting(Path.Combine(roaming,"Hiddify"),Path.Combine(roaming,"com.hiddify.app"),Path.Combine(roaming,"app.hiddify.com"),Path.Combine(local,"Hiddify"),Path.Combine(local,"app.hiddify.com"))),
            new SingBoxDefinition("nekobox", "NekoBox / NekoRay", FirstExisting(Path.Combine(roaming,"nekobox"),Path.Combine(roaming,"nekoray"))),
            new SingBoxDefinition("gui-for-singbox", "GUI.for.SingBox", FirstExisting(Path.Combine(roaming,"gui-for-singbox"),Path.Combine(roaming,"GUI.for.SingBox"))),
            new SingBoxDefinition("sing-box", "sing-box", Path.Combine(roaming,"sing-box"))
        };
        return definitions.Where(x => Directory.Exists(x.Directory)).Select(x => new SingBoxConfigAdapter(x));
    }

    public Task<List<NodeInfo>> GetNodesAsync()
    {
        var result = new List<NodeInfo>();
        foreach (var path in _configs)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                if (!json.RootElement.TryGetProperty("outbounds", out var outbounds)) continue;
                foreach (var outbound in outbounds.EnumerateArray())
                {
                    var type = Text(outbound, "type"); var tag = Text(outbound, "tag"); var server = Text(outbound, "server");
                    var port = outbound.TryGetProperty("server_port", out var p) && p.TryGetInt32(out var n) ? n : 0;
                    if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(server) || type is "direct" or "block" or "dns" or "selector" or "urltest") continue;
                    var native = $"{path}\n{tag}";
                    result.Add(new(Key(Id, native), native, Id, Name, tag, Path.GetFileName(path), 0, server, port));
                }
            }
            catch { }
        }
        return Task.FromResult(result.DistinctBy(x => x.Id).ToList());
    }
    public Task<List<TestResult>> RunDelayAsync(IReadOnlyList<NodeInfo> nodes, CancellationToken cancellationToken) => Task.FromResult(new List<TestResult>());
    public Task SwitchAsync(NodeInfo node) => throw new NotSupportedException($"{Name} 当前为只读配置适配。");
    private static List<string> FindConfigs(string directory)
    {
        try { return Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Where(x => new FileInfo(x).Length < 10_000_000).Take(100).ToList(); }
        catch { return []; }
    }
    private static string Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
    private static string FirstExisting(params string[] paths) => paths.FirstOrDefault(Directory.Exists) ?? paths[0];
    private static string Key(string client, string value) => client + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
