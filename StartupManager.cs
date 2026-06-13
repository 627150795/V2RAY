using Microsoft.Win32;

namespace V2rayNMonitor;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProxyMonitor";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe)) key.SetValue(ValueName, $"\"{exe}\" --background");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }

        RemoveLegacyStartupShortcuts();
    }

    private static void RemoveLegacyStartupShortcuts()
    {
        var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        foreach (var name in new[] { "ProxyMonitor.lnk", "多客户端节点监控.lnk" })
        {
            var path = Path.Combine(startup, name);
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
