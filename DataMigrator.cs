using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace V2rayNMonitor;

public static class DataMigrator
{
    public static int MigrateOldData()
    {
        try
        {
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V2rayNMonitor");
            if (!Directory.Exists(oldDir))
            {
                Console.WriteLine("旧版数据目录不存在，无需迁移。");
                return 0;
            }

            Directory.CreateDirectory(Paths.DataDir);
            var backup = Path.Combine(Paths.DataDir, "migration-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            CopyDataFiles(oldDir, Path.Combine(backup, "old"));
            CopyDataFiles(Paths.DataDir, Path.Combine(backup, "new"));

            MigrateSettings(Path.Combine(oldDir, "settings.json"));
            var imported = MigrateHistory(Path.Combine(oldDir, "history.db"));
            Console.WriteLine($"迁移完成：导入 {imported} 条历史结果；备份：{backup}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"迁移失败：{ex}");
            return 1;
        }
    }

    private static void CopyDataFiles(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var name in new[] { "settings.json", "history.db", "history.db-wal", "history.db-shm" })
        {
            var path = Path.Combine(source, name);
            if (File.Exists(path)) File.Copy(path, Path.Combine(destination, name), true);
        }
    }

    private static void MigrateSettings(string oldSettingsPath)
    {
        if (!File.Exists(oldSettingsPath)) return;
        var old = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(oldSettingsPath));
        if (old is null) return;
        old.EnableV2rayN = true;
        old.EnableMihomo = true;
        Paths.SaveSettings(old);
    }

    private static int MigrateHistory(string oldHistoryPath)
    {
        if (!File.Exists(oldHistoryPath)) return 0;
        _ = new HistoryStore();
        using var db = new SqliteConnection($"Data Source={Paths.HistoryFile}");
        db.Open();
        using var tx = db.BeginTransaction();
        using var attach = db.CreateCommand();
        attach.Transaction = tx;
        attach.CommandText = "ATTACH DATABASE $old AS olddb";
        attach.Parameters.AddWithValue("$old", oldHistoryPath);
        attach.ExecuteNonQuery();

        using var command = db.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO results(node_id,kind,ts,delay_ms,jitter_ms,success_rate,speed_bps,error)
            SELECT 'v2rayn:' || o.node_id,o.kind,o.ts,o.delay_ms,o.jitter_ms,o.success_rate,o.speed_bps,o.error
            FROM olddb.results o
            WHERE NOT EXISTS (
              SELECT 1 FROM results n
              WHERE n.node_id='v2rayn:' || o.node_id AND n.kind=o.kind AND n.ts=o.ts
                AND IFNULL(n.delay_ms,-1)=IFNULL(o.delay_ms,-1)
                AND IFNULL(n.speed_bps,-1)=IFNULL(o.speed_bps,-1)
            );
            """;
        var imported = command.ExecuteNonQuery();
        tx.Commit();
        return imported;
    }
}
