using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;

namespace TimeSink.Tests.Data;

public sealed class DbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public void Opening_a_new_database_creates_the_initial_schema_and_accepts_an_app_row()
    {
        var path = Path.Combine(_dir, "timesink.db");

        using (var db = Db.Open(path))
        {
            db.SchemaVersion.Should().Be(Db.LatestSchemaVersion);
            db.JournalMode.Should().Be("wal");
            var id = db.InsertApp(@"c:\windows\notepad.exe", "Notepad", addedUtc: 1_700_000_000);
            id.Should().BeGreaterThan(0);
        }

        using var reopened = Db.Open(path);
        reopened.SchemaVersion.Should().Be(Db.LatestSchemaVersion);
        reopened.GetApps().Should().ContainSingle()
            .Which.Should().Be(new AppRow(1, @"c:\windows\notepad.exe", "Notepad", "tracked"));
    }

    [Fact]
    public void Exe_paths_are_unique_case_insensitively()
    {
        using var db = Db.Open(Path.Combine(_dir, "timesink.db"));
        db.InsertApp(@"C:\Tools\App.exe", "App", 1);
        var act = () => db.InsertApp(@"c:\tools\app.EXE", "App again", 2);
        act.Should().Throw<SqliteException>();
    }
}
