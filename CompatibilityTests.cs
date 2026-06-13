using Microsoft.Data.Sqlite;

namespace V2rayNMonitor;

public static class CompatibilityTests
{
    public static List<(string Name, bool Passed, string Detail)> Run()
    {
        var root = Path.Combine(Paths.DataDir, "compatibility-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var results = new List<(string, bool, string)>();
        try
        {
            Test(results, root, "7.12-7.22 标准结构", StandardSchema(), expected: 1);
            Test(results, root, "6.23-6.60 小写字段结构", LowercaseSchema(), expected: 1);
            Test(results, root, "可选字段缺失结构", OptionalFieldsMissingSchema(), expected: 2);
            Test(results, root, "复数表名变体", PluralTableSchema(), expected: 1);
            Test(results, root, "不兼容结构明确拒绝", BrokenSchema(), expected: null);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
        return results;
    }

    private static void Test(List<(string, bool, string)> results, string root, string name, string schema, int? expected)
    {
        var path = Path.Combine(root, $"{results.Count}.db");
        using (var db = new SqliteConnection($"Data Source={path}"))
        {
            db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = schema; cmd.ExecuteNonQuery();
        }
        try
        {
            var compatibility = V2rayNReader.Inspect("", path);
            if (expected is null)
            {
                results.Add((name, !compatibility.CanRead && compatibility.MissingFields.Count > 0, compatibility.Summary));
                return;
            }
            var nodes = V2rayNReader.ReadEnabledNodes(path);
            results.Add((name, compatibility.CanRead && nodes.Count == expected, $"{compatibility.Summary}; 节点={nodes.Count}"));
        }
        catch (Exception ex) { results.Add((name, false, ex.Message)); }
    }

    private static string StandardSchema() => """
        CREATE TABLE ProfileItem(IndexId TEXT, ConfigType INTEGER, Subid TEXT, IsSub INTEGER, Remarks TEXT, Address TEXT, Port INTEGER);
        CREATE TABLE SubItem(Id TEXT, Remarks TEXT, Enabled INTEGER, Sort INTEGER);
        INSERT INTO SubItem VALUES('on','启用订阅',1,1),('off','禁用订阅',0,2);
        INSERT INTO ProfileItem VALUES('normal',1,'on',1,'正常节点','a.example',443);
        INSERT INTO ProfileItem VALUES('custom',2,'on',1,'Custom节点','b.example',443);
        INSERT INTO ProfileItem VALUES('manual',1,'on',0,'手工节点','c.example',443);
        INSERT INTO ProfileItem VALUES('disabled',1,'off',1,'禁用订阅节点','d.example',443);
        """;
    private static string OptionalFieldsMissingSchema() => """
        CREATE TABLE ProfileItem(IndexId TEXT, ConfigType INTEGER, Subid TEXT, Remarks TEXT, Address TEXT, Port INTEGER);
        CREATE TABLE SubItem(Id TEXT, Remarks TEXT);
        INSERT INTO SubItem VALUES('sub','旧版订阅');
        INSERT INTO ProfileItem VALUES('a',1,'sub','节点 A','a.example',443),('b',3,'sub','节点 B','b.example',8443);
        """;
    private static string LowercaseSchema() => """
        CREATE TABLE ProfileItem(indexId TEXT, configType INTEGER, subid TEXT, isSub INTEGER, remarks TEXT, address TEXT, port INTEGER);
        CREATE TABLE SubItem(id TEXT, remarks TEXT, enabled INTEGER, sort INTEGER);
        INSERT INTO SubItem VALUES('sub','6.x 订阅',1,0);
        INSERT INTO ProfileItem VALUES('a',1,'sub',1,'6.x 节点','a.example',443);
        """;
    private static string PluralTableSchema() => """
        CREATE TABLE ProfileItems(IndexId TEXT, ConfigType INTEGER, Subid TEXT, IsSub INTEGER, Remarks TEXT, Address TEXT, Port INTEGER);
        CREATE TABLE SubItems(Id TEXT, Remarks TEXT, Enabled INTEGER, Sort INTEGER);
        INSERT INTO SubItems VALUES('sub','复数表订阅',1,0);
        INSERT INTO ProfileItems VALUES('a',1,'sub',1,'节点 A','a.example',443);
        """;
    private static string BrokenSchema() => """
        CREATE TABLE ProfileItem(IndexId TEXT, ConfigType INTEGER, Remarks TEXT);
        CREATE TABLE SubItem(Id TEXT, Remarks TEXT);
        """;
}
