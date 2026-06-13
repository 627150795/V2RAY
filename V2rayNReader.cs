using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace V2rayNMonitor;

public sealed record V2rayNCompatibility(
    string Version, string DatabasePath, string ConfigPath, string ProfileTable, string SubscriptionTable,
    bool CanRead, bool CanSwitch, bool CanTest, IReadOnlyList<string> MissingFields, string Summary);

public static class V2rayNReader
{
    private static readonly string[] ProfileTables = ["ProfileItem", "ProfileItems"];
    private static readonly string[] SubscriptionTables = ["SubItem", "SubItems"];
    private static readonly string[] RequiredProfile = ["IndexId", "ConfigType", "Subid", "Remarks", "Address", "Port"];
    private static readonly string[] RequiredSubscription = ["Id", "Remarks"];

    public static V2rayNCompatibility Inspect(string exePath, string? dbOverride = null)
    {
        var version = File.Exists(exePath) ? FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "未知" : "未知";
        var dbPath = dbOverride ?? LocateDatabase(exePath) ?? "";
        var configPath = LocateConfig(exePath) ?? "";
        if (!File.Exists(dbPath))
            return new(version, dbPath, configPath, "", "", false, false, false, ["guiNDB.db"], "找不到节点数据库");

        using var db = Open(dbPath);
        var tables = Tables(db);
        var profile = ProfileTables.FirstOrDefault(tables.Contains) ?? "";
        var subscription = SubscriptionTables.FirstOrDefault(tables.Contains) ?? "";
        var missing = new List<string>();
        if (profile.Length == 0) missing.Add("ProfileItem/ProfileItems");
        if (subscription.Length == 0) missing.Add("SubItem/SubItems");
        if (profile.Length > 0) missing.AddRange(RequiredProfile.Where(x => !Columns(db, profile).Contains(x)).Select(x => $"{profile}.{x}"));
        if (subscription.Length > 0) missing.AddRange(RequiredSubscription.Where(x => !Columns(db, subscription).Contains(x)).Select(x => $"{subscription}.{x}"));
        var canRead = missing.Count == 0;
        var canSwitch = canRead && File.Exists(configPath) && ConfigHasIndexId(configPath);
        var root = Path.GetDirectoryName(exePath) ?? "";
        var major = int.TryParse(version.Split('.')[0], out var parsedMajor) ? parsedMajor : 0;
        var canTest = canRead && Directory.Exists(Path.Combine(root, "bin")) && major >= 7;
        var summary = canRead
            ? $"兼容结构；读取={(canRead ? "是" : "否")}，测试={(canTest ? "是" : "否")}，切换={(canSwitch ? "是" : "否")}"
            : $"不兼容结构；缺少 {string.Join(", ", missing)}";
        return new(version, dbPath, configPath, profile, subscription, canRead, canSwitch, canTest, missing, summary);
    }

    public static string MakeSnapshot(string exePath, string targetDir)
    {
        var sourceDb = LocateDatabase(exePath) ?? throw new FileNotFoundException("找不到 v2rayN 节点数据库。");
        var sourceDir = Path.GetDirectoryName(sourceDb)!;
        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        return Path.Combine(targetDir, Path.GetFileName(sourceDb));
    }

    public static List<NodeInfo> ReadEnabledNodes(string dbPath)
    {
        var compatibility = Inspect("", dbPath);
        if (!compatibility.CanRead) throw new InvalidDataException(compatibility.Summary);
        using var db = Open(dbPath);
        var profileColumns = Columns(db, compatibility.ProfileTable);
        var subscriptionColumns = Columns(db, compatibility.SubscriptionTable);
        var isSub = profileColumns.Contains("IsSub") ? "COALESCE(p.[IsSub],1)=1" : "1=1";
        var enabled = subscriptionColumns.Contains("Enabled") ? "COALESCE(s.[Enabled],1)=1" : "1=1";
        var sort = subscriptionColumns.Contains("Sort") ? "COALESCE(s.[Sort],0)" : "s.[Remarks]";
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"""
            SELECT p.[IndexId], COALESCE(p.[Remarks],''), COALESCE(s.[Remarks],''),
                   COALESCE(p.[ConfigType],0), COALESCE(p.[Address],''), COALESCE(p.[Port],0)
            FROM [{compatibility.ProfileTable}] p
            JOIN [{compatibility.SubscriptionTable}] s ON s.[Id]=p.[Subid]
            WHERE {enabled} AND {isSub} AND COALESCE(p.[ConfigType],0)<>2
            ORDER BY {sort}, p.[Remarks]
            """;
        using var reader = cmd.ExecuteReader();
        var list = new List<NodeInfo>();
        while (reader.Read())
        {
            var nativeId = Convert.ToString(reader.GetValue(0)) ?? "";
            list.Add(new($"v2rayn:{nativeId}", nativeId, "v2rayn", "v2rayN",
                Convert.ToString(reader.GetValue(1)) ?? "", Convert.ToString(reader.GetValue(2)) ?? "",
                Convert.ToInt32(reader.GetValue(3)), Convert.ToString(reader.GetValue(4)) ?? "", Convert.ToInt32(reader.GetValue(5))));
        }
        return list;
    }

    public static bool NodeExists(string exePath, string nodeId)
    {
        var dbPath = MakeSnapshot(exePath, Path.Combine(Paths.DataDir, $"switch-check-{Environment.ProcessId}"));
        return ReadEnabledNodes(dbPath).Any(x => x.NativeId == nodeId);
    }

    public static string? LocateDatabase(string exePath)
    {
        var root = Path.GetDirectoryName(exePath);
        if (root is null) return null;
        return new[] { Path.Combine(root, "guiConfigs", "guiNDB.db"), Path.Combine(root, "guiNDB.db") }.FirstOrDefault(File.Exists);
    }

    public static string? LocateConfig(string exePath)
    {
        var root = Path.GetDirectoryName(exePath);
        if (root is null) return null;
        return new[] { Path.Combine(root, "guiConfigs", "guiNConfig.json"), Path.Combine(root, "guiNConfig.json") }.FirstOrDefault(File.Exists);
    }

    private static SqliteConnection Open(string path)
    {
        var db = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        db.Open();
        return db;
    }
    private static HashSet<string> Tables(SqliteConnection db)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var reader = cmd.ExecuteReader(); var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) result.Add(reader.GetString(0)); return result;
    }
    private static HashSet<string> Columns(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = $"PRAGMA table_info([{table}])";
        using var reader = cmd.ExecuteReader(); var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) result.Add(reader.GetString(1)); return result;
    }
    private static bool ConfigHasIndexId(string path)
    {
        try { return File.ReadAllText(path).Contains("\"IndexId\"", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
