with open('tests/TimeSink.Tests/Sync/SyncWriterReaderTests.cs', 'r', encoding='utf-8') as f:
    text = f.read()

target = r'''        rig.Exec(srcDb, "INSERT INTO apps (exe_path, display_name, category, state, added_utc, is_background, kind, device_id) VALUES ('C:\\Tools\\Antigravity.exe', 'Antigravity', 'Other', 'tracked', 100, 0, 0, 'dev-win-001');");'''

replacement = r'''        rig.Exec(srcDb, "INSERT INTO devices (device_id, name, platform, is_local, first_seen_utc, state) VALUES ('dev-win-001', 'Desktop PC', 'windows', 0, '2026-11-01T00:00:00Z', 'active');");
        rig.Exec(srcDb, "INSERT INTO apps (exe_path, display_name, category, state, added_utc, is_background, kind, device_id) VALUES ('C:\\Tools\\Antigravity.exe', 'Antigravity', 'Other', 'tracked', 100, 0, 0, 'dev-win-001');");'''

text = text.replace(target, replacement)
with open('tests/TimeSink.Tests/Sync/SyncWriterReaderTests.cs', 'w', encoding='utf-8', newline='\r\n') as f:
    f.write(text)
print('Inserted dev-win-001 in devices table first')
