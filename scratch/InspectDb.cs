using System;
using System.IO;
using TimeSink.Core.Apps;
using TimeSink.Core.Data;

var dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "timesink.db");
Console.WriteLine($"DB Path: {dbPath}, Exists: {File.Exists(dbPath)}");

using var db = Db.Open(dbPath);
using var cmd = db.Connection.CreateCommand();
cmd.CommandText = "SELECT id, canonical_id, display_name, exe_path, kind, is_background, state, device_id FROM apps WHERE display_name LIKE '%Antigravity%' OR exe_path LIKE '%Antigravity%';";
using var r = cmd.ExecuteReader();
while (r.Read())
{
    var id = r.GetInt64(0);
    var canon = r.IsDBNull(1) ? (long?)null : r.GetInt64(1);
    var name = r.GetString(2);
    var exe = r.GetString(3);
    var kind = r.IsDBNull(4) ? (long?)null : r.GetInt64(4);
    var bg = r.GetInt64(5);
    var state = r.GetString(6);
    var dev = r.IsDBNull(7) ? "" : r.GetString(7);
    Console.WriteLine($"ID: {id}, Canon: {canon}, Name: '{name}', Exe: '{exe}', Kind: {kind}, Bg: {bg}, State: '{state}', Dev: '{dev}'");
}

