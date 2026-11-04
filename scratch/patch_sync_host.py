path_host = 'src/TimeSink.Core/Sync/SyncHost.cs'
with open(path_host, 'r', encoding='utf-8') as f:
    code = f.read()

target1 = """    private DateTimeOffset _lastExportUtc = DateTimeOffset.MinValue;
    private long _lastSessionCount = -1;
    private long _lastMaxLastSeen = -1;"""

rep1 = """    private DateTimeOffset _lastExportUtc = DateTimeOffset.MinValue;
    private long _lastSessionCount = -1;
    private long _lastMaxLastSeen = -1;
    private long _lastAppsFingerprint = -1;"""

assert target1 in code or target1.replace('\n', '\r\n') in code, "target1 not found"
code = code.replace(target1.replace('\n', '\r\n'), rep1.replace('\n', '\r\n')).replace(target1, rep1)

target2 = """            // 1. Export local snapshot if due or forced
            var sessionsChanged = HasSessionsChanged(db, localDeviceId, out var count, out var maxLastSeen);
            var exportDue = forceExport || _lastExportUtc == DateTimeOffset.MinValue || (now - _lastExportUtc >= _exportInterval && sessionsChanged);

            if (exportDue)
            {
                try
                {
                    SyncWriter.Write(db.Connection, folder, now, keyManager: _keyManager);
                    _lastExportUtc = now;
                    _lastSessionCount = count;
                    _lastMaxLastSeen = maxLastSeen;"""

rep2 = """            // 1. Export local snapshot if due or forced
            var sessionsChanged = HasSessionsChanged(db, localDeviceId, out var count, out var maxLastSeen);
            var appsChanged = HasAppsChanged(db, localDeviceId, out var appsFingerprint);
            var exportDue = forceExport || _lastExportUtc == DateTimeOffset.MinValue || (now - _lastExportUtc >= _exportInterval && sessionsChanged) || appsChanged;

            if (exportDue)
            {
                try
                {
                    SyncWriter.Write(db.Connection, folder, now, keyManager: _keyManager);
                    _lastExportUtc = now;
                    _lastSessionCount = count;
                    _lastMaxLastSeen = maxLastSeen;
                    _lastAppsFingerprint = appsFingerprint;"""

assert target2 in code or target2.replace('\n', '\r\n') in code, "target2 not found"
code = code.replace(target2.replace('\n', '\r\n'), rep2.replace('\n', '\r\n')).replace(target2, rep2)

target3 = """        return count != _lastSessionCount || maxLastSeen != _lastMaxLastSeen;
    }"""

rep3 = """        return count != _lastSessionCount || maxLastSeen != _lastMaxLastSeen;
    }

    private bool HasAppsChanged(Db db, string localDeviceId, out long fingerprint)
    {
        fingerprint = 0;
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = \"\"\"
            SELECT COUNT(*), COALESCE(SUM(kind), 0), COALESCE(SUM(is_background), 0), COALESCE(SUM(COALESCE(canonical_id, 0)), 0), COALESCE(MAX(added_utc), 0)
            FROM apps
            WHERE device_id = $devId
               OR id IN (SELECT canonical_id FROM apps WHERE device_id = $devId AND canonical_id IS NOT NULL)
               OR canonical_id IN (SELECT id FROM apps WHERE device_id = $devId);
            \"\"\";
        cmd.Parameters.AddWithValue("$devId", localDeviceId);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            fingerprint = HashCode.Combine(r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), HashCode.Combine(r.GetInt64(3), r.GetInt64(4)));
        }

        return _lastAppsFingerprint != -1 && fingerprint != _lastAppsFingerprint;
    }"""

assert target3 in code or target3.replace('\n', '\r\n') in code, "target3 not found"
code = code.replace(target3.replace('\n', '\r\n'), rep3.replace('\n', '\r\n')).replace(target3, rep3)

with open(path_host, 'w', encoding='utf-8') as f:
    f.write(code)
print("SyncHost.cs patched successfully")

