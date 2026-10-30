using Microsoft.Win32;

namespace TimeSink.Core.Settings;

/// <summary>The HKCU Run key, abstracted so Start with Windows can be tested with a fake.</summary>
public interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Remove(string name);
}

/// <summary>Start with Windows: owns the <c>TimeSink</c> value under HKCU Run (same value the installer writes).</summary>
public sealed class Autostart(IRunKey key, string agentExePath)
{
    public const string ValueName = "TimeSink";

    public string Command => $"\"{agentExePath}\" --background";

    public bool IsEnabled => key.Get(ValueName) is { Length: > 0 };

    public void SetEnabled(bool enabled)
    {
        if (enabled) key.Set(ValueName, Command);
        else key.Remove(ValueName);
    }

    /// <summary>
    /// Start with Windows defaults to on and the stored setting is the user's choice. While it is on, the Run value is
    /// (re)written when missing or pointing elsewhere: uninstalling removes the value but keeps the database, so a
    /// reinstall would otherwise show the setting on with nothing registered.
    /// </summary>
    public void Reconcile(ISettingsSource settings)
    {
        var stored = settings.Get(Setting.Startup.Key);
        if (stored is null) settings.Set(Setting.Startup.Key, Setting.Startup.DefaultText);
        if (!Setting.Startup.Parse(stored)) return;
        if (key.Get(ValueName) != Command) SetEnabled(true);
    }
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class RegistryRunKey(string subKeyPath = RegistryRunKey.DefaultPath) : IRunKey
{
    public const string DefaultPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKeyPath);
        return k?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var k = Registry.CurrentUser.CreateSubKey(subKeyPath);
        k.SetValue(name, value, RegistryValueKind.String);
    }

    public void Remove(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }
}
