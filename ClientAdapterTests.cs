namespace V2rayNMonitor;

public static class ClientAdapterTests
{
    public static async Task<List<(string Name, bool Passed, string Detail)>> RunAsync()
    {
        var root = Path.Combine(Paths.DataDir, "client-adapter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<(string, bool, string)>();
        try
        {
            var mihomo = Path.Combine(root, "mihomo"); Directory.CreateDirectory(mihomo);
            File.WriteAllText(Path.Combine(mihomo, "config.yaml"), "external-controller: ''\nsecret: test\n");
            File.WriteAllText(Path.Combine(mihomo, "merged.yaml"), """
                proxies:
                  - name: Node A
                    type: ss
                    server: a.example
                    port: 443
                  - name: Node B
                    type: trojan
                    server: b.example
                    port: 8443
                """);
            var mihomoAdapter = new MihomoAdapter(new("test-mihomo", "测试 Mihomo", mihomo, ["config.yaml", "merged.yaml"]));
            var mihomoNodes = await mihomoAdapter.GetNodesAsync();
            results.Add(("Mihomo 离线 YAML", mihomoNodes.Count == 2 && mihomoAdapter.Capability.CanRead && !mihomoAdapter.Capability.CanDelay, $"节点={mihomoNodes.Count}"));

            var singbox = Path.Combine(root, "singbox"); Directory.CreateDirectory(singbox);
            File.WriteAllText(Path.Combine(singbox, "config.json"), """
                {"outbounds":[
                  {"type":"direct","tag":"direct"},
                  {"type":"vless","tag":"Node A","server":"a.example","server_port":443},
                  {"type":"trojan","tag":"Node B","server":"b.example","server_port":8443}
                ]}
                """);
            var singAdapter = new SingBoxConfigAdapter(new("test-sing", "测试 sing-box", singbox));
            var singNodes = await singAdapter.GetNodesAsync();
            results.Add(("sing-box JSON 只读", singNodes.Count == 2 && singAdapter.Capability.CanRead && !singAdapter.Capability.CanDelay, $"节点={singNodes.Count}"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        return results;
    }
}
