using System.Diagnostics;
using System.Text.Json.Nodes;

namespace V2rayNMonitor;

public static class Switcher
{
    public static async Task SwitchAsync(string exePath,string nodeId)
    {
        if (nodeId.StartsWith("v2rayn:", StringComparison.Ordinal)) nodeId = nodeId["v2rayn:".Length..];
        if(!V2rayNReader.NodeExists(exePath,nodeId)) throw new InvalidOperationException("目标节点已不存在或不属于启用订阅。");
        var root=Path.GetDirectoryName(exePath)!; var config=V2rayNReader.LocateConfig(exePath)
            ?? throw new FileNotFoundException("找不到 v2rayN 主配置文件。");
        var backup=config+$".monitor-{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Copy(config,backup,true);
        try
        {
            foreach(var process in Process.GetProcesses())
            {
                try { if(process.MainModule?.FileName?.StartsWith(root,StringComparison.OrdinalIgnoreCase)==true) process.Kill(true); } catch { }
            }
            await Task.Delay(700);
            var json=JsonNode.Parse(await File.ReadAllTextAsync(config))?.AsObject()??throw new InvalidDataException("配置 JSON 无效");
            UpdateIndexId(json,nodeId);
            var temp=config+".monitor.tmp"; await File.WriteAllTextAsync(temp,json.ToJsonString(new(){WriteIndented=true}));
            File.Move(temp,config,true);
            var p=Process.Start(new ProcessStartInfo(exePath){WorkingDirectory=root,UseShellExecute=true});
            await Task.Delay(2500);
            if(p is null||p.HasExited) throw new InvalidOperationException("v2rayN 重启验证失败");
        }
        catch
        {
            File.Copy(backup,config,true);
            Process.Start(new ProcessStartInfo(exePath){WorkingDirectory=root,UseShellExecute=true});
            throw;
        }
    }

    public static void UpdateIndexId(JsonObject json, string nodeId)
    {
        var key = json.Select(x => x.Key).FirstOrDefault(x => x.Equals("IndexId", StringComparison.OrdinalIgnoreCase));
        if (key is null) throw new InvalidDataException("配置中缺少 IndexId/indexId。");
        json[key] = nodeId;
    }
}
