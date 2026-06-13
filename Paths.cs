using System.Diagnostics;
using System.Text.Json;

namespace V2rayNMonitor;

public static class Paths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProxyMonitor");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string HistoryFile => Path.Combine(DataDir, "history.db");
    public static string SandboxDir => Path.Combine(DataDir, "worker");

    public static MonitorSettings LoadSettings()
    {
        Directory.CreateDirectory(DataDir);
        try
        {
            var settings = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(SettingsFile)) ?? new();
            if (settings.SpeedLimitBytes <= 1_048_576) settings.SpeedLimitBytes = 2_097_152;
            if (settings.RefineSpeedLimitBytes < settings.SpeedLimitBytes) settings.RefineSpeedLimitBytes = 10_485_760;
            if (settings.RefineTopCount <= 0) settings.RefineTopCount = 10;
            if (settings.SpeedTimeoutSeconds <= 0) settings.SpeedTimeoutSeconds = 5;
            return settings;
        }
        catch { return new(); }
    }

    public static void SaveSettings(MonitorSettings value)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string? LocateV2rayN()
    {
        var running = Process.GetProcessesByName("v2rayN").FirstOrDefault()?.MainModule?.FileName;
        if (File.Exists(running)) return running;
        var settings = LoadSettings();
        if (File.Exists(settings.V2rayNPath)) return settings.V2rayNPath;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "v2rayN", "v2rayN.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "v2rayN", "v2rayN.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
