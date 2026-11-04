with open('src/TimeSink.Core/Sync/SyncReader.cs', 'r', encoding='utf-8') as f:
    text = f.read()

target = r'''                            if (existingId is long id)
                            {
                                dstId = id;
                                using var updCmd = conn.CreateCommand();
                                updCmd.CommandText = """
                                    UPDATE main.apps SET
                                        exe_path = $p,
                                        bundle_id = CASE WHEN $b IS NOT NULL AND $b != '' THEN $b ELSE bundle_id END,
                                        package_family = CASE WHEN $f IS NOT NULL AND $f != '' THEN $f ELSE package_family END,
                                        display_name = $n,
                                        category = $c,
                                        state = $s,
                                        retired_utc = $r,
                                        is_background = $bg,
                                        kind = $k
                                    WHERE id = $id;
                                    """;
                                updCmd.Parameters.AddWithValue("$p", exePath);
                                updCmd.Parameters.AddWithValue("$b", (object?)bundleId ?? DBNull.Value);
                                updCmd.Parameters.AddWithValue("$f", (object?)packageFamily ?? DBNull.Value);
                                updCmd.Parameters.AddWithValue("$n", displayName);
                                updCmd.Parameters.AddWithValue("$c", category);
                                updCmd.Parameters.AddWithValue("$s", state);
                                updCmd.Parameters.AddWithValue("$r", (object?)retiredUtc ?? DBNull.Value);
                                updCmd.Parameters.AddWithValue("$bg", isBackground);
                                updCmd.Parameters.AddWithValue("$k", effectiveKind);
                                updCmd.Parameters.AddWithValue("$id", dstId);
                                updCmd.ExecuteNonQuery();
                            }'''

replacement = r'''                            if (existingId is long id)
                            {
                                dstId = id;
                                if (string.Equals(devId, manifest.DeviceId, StringComparison.OrdinalIgnoreCase))
                                {
                                    using var updCmd = conn.CreateCommand();
                                    updCmd.CommandText = """
                                        UPDATE main.apps SET
                                            exe_path = $p,
                                            bundle_id = CASE WHEN $b IS NOT NULL AND $b != '' THEN $b ELSE bundle_id END,
                                            package_family = CASE WHEN $f IS NOT NULL AND $f != '' THEN $f ELSE package_family END,
                                            display_name = $n,
                                            category = $c,
                                            state = $s,
                                            retired_utc = $r,
                                            is_background = $bg,
                                            kind = $k
                                        WHERE id = $id;
                                        """;
                                    updCmd.Parameters.AddWithValue("$p", exePath);
                                    updCmd.Parameters.AddWithValue("$b", (object?)bundleId ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$f", (object?)packageFamily ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$n", displayName);
                                    updCmd.Parameters.AddWithValue("$c", category);
                                    updCmd.Parameters.AddWithValue("$s", state);
                                    updCmd.Parameters.AddWithValue("$r", (object?)retiredUtc ?? DBNull.Value);
                                    updCmd.Parameters.AddWithValue("$bg", isBackground);
                                    updCmd.Parameters.AddWithValue("$k", effectiveKind);
                                    updCmd.Parameters.AddWithValue("$id", dstId);
                                    updCmd.ExecuteNonQuery();
                                }
                            }'''

text_norm = text.replace('\r\n', '\n')
target_norm = target.replace('\r\n', '\n')
replacement_norm = replacement.replace('\r\n', '\n')

if target_norm in text_norm:
    text_norm = text_norm.replace(target_norm, replacement_norm, 1)
    with open('src/TimeSink.Core/Sync/SyncReader.cs', 'w', encoding='utf-8', newline='\r\n') as f:
        f.write(text_norm)
    print('Fixed SyncReader to protect local apps')
else:
    print('Failed to find target in SyncReader.cs')

