using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class UsageDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-usagedb-" + Guid.NewGuid().ToString("N"));

    public UsageDbTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string MakeDb()
    {
        var path = Path.Combine(_dir, "t.db");
        using (Db.Open(path)) { }
        return path;
    }

    private static long Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    [Fact]
    public void Opens_with_a_two_second_busy_wait()
    {
        using var c = UsageDb.OpenReadOnly(MakeDb());
        Assert.Equal(2000, Scalar(c, "PRAGMA busy_timeout"));
    }

    [Fact]
    public void Opens_read_only()
    {
        using var c = UsageDb.OpenReadOnly(MakeDb());
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE x (a INTEGER)";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    [Fact]
    public void A_missing_file_throws_and_is_not_created()
    {
        var path = Path.Combine(_dir, "missing.db");
        Assert.Throws<SqliteException>(() => UsageDb.OpenReadOnly(path));
        Assert.False(File.Exists(path));
    }
}
