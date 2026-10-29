using TimeSink.Core.Data;

namespace TimeSink.Core.Settings;

/// <summary>Where stored settings text lives: the <c>settings</c> table, reached one of two ways.</summary>
public interface ISettingsSource
{
    string? Get(string key);

    void Set(string key, string value);
}

/// <summary>The UI's and helpers' way in: its own short connection, best effort, never migrates (decision 35).</summary>
public sealed class FileSettingsSource(string dbPath) : ISettingsSource
{
    public string? Get(string key) => SettingsStore.Get(dbPath, key);

    public void Set(string key, string value) => SettingsStore.Set(dbPath, key, value);
}

/// <summary>The Agent's way in: the connection it already holds open.</summary>
public sealed class DbSettingsSource(Db db) : ISettingsSource
{
    public string? Get(string key) => db.GetSetting(key);

    public void Set(string key, string value) => db.SetSetting(key, value);
}

/// <summary>
/// Reads and writes <see cref="Setting{T}"/> values. A missing or invalid stored value reads as the setting's default, so a
/// caller never parses text or knows a key.
/// </summary>
public sealed class AppSettings(ISettingsSource source)
{
    public static AppSettings ForFile(string dbPath) => new(new FileSettingsSource(dbPath));

    public static AppSettings ForDb(Db db) => new(new DbSettingsSource(db));

    public T Get<T>(Setting<T> setting) => setting.Parse(source.Get(setting.Key));

    public void Set<T>(Setting<T> setting, T value) => source.Set(setting.Key, setting.Format(value));
}
