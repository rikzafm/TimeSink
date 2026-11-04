with open('tests/TimeSink.Tests/Apps/AppLibraryTests.cs', 'r', encoding='utf-8') as f:
    text = f.read()

idx = text.find('    [Fact]\n    public void InspectUserDb()')
if idx == -1:
    idx = text.find('    [Fact]\r\n    public void InspectUserDb()')

new_code = r'''    [Fact]
    public void InspectUserDb()
    {
        var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "timesink.db");
        if (!File.Exists(dbPath)) return;
        using (var lib = AppLibrary.Open(dbPath))
        {
            lib.SetKind(5, AppKind.Coding);
        }
        var syncFile = @"C:\Users\SNOW\OneDrive\Apps\TimeSink\E2216855-2EB4-488C-B177-495F8614E5B4.tssync";
        TimeSink.Core.Sync.SyncReader.Import(syncFile, dbPath, TimeZoneInfo.Local);
        using var checkDb = Db.Open(dbPath);
        using var checkCmd = checkDb.Connection.CreateCommand();
        checkCmd.CommandText = "SELECT id, kind, canonical_id FROM apps WHERE id IN (5, 54);";
        using var r = checkCmd.ExecuteReader();
        var s = "";
        while (r.Read()) s += $"id {r.GetInt64(0)} kind {r.GetInt64(1)} canon {r.GetValue(2)}, ";
        throw new Exception($"After Import with protection: {s}");
    }
}'''

base = text[:idx]
with open('tests/TimeSink.Tests/Apps/AppLibraryTests.cs', 'w', encoding='utf-8', newline='\r\n') as f:
    f.write(base + new_code)
print('Rewrote InspectUserDb cleanly')
