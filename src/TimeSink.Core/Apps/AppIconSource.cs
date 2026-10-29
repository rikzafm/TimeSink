using Microsoft.Data.Sqlite;
using TimeSink.Core.Data;
using TimeSink.Core.Icons;

namespace TimeSink.Core.Apps;

/// <summary>
/// Where every App icon comes from, as PNG bytes. <see cref="ForApps"/> answers by app id: the stored icon, or one
/// extracted from its exe (Steam's cached art instead when the exe's own icon is tiny or unreadable) and stored when
/// the app has none; TimeSink's own row always shows the chosen tray icon. The first resolve of a database also
/// re-extracts every stored Steam icon once, since those used to be Steam's 32 px icon scaled up. <see cref="ForPath"/> answers by exe path and size, for apps that have no id yet. <see cref="Stored"/>
/// reads what is stored and never extracts. <see cref="AppLibrary"/> stays beneath it as the SQLite adapter and
/// <see cref="IDesktop"/> is the seam to icon extraction. A busy or missing database
/// (<see cref="SqliteException"/>, <see cref="IOException"/>) leaves the apps without an icon, so their letter tiles
/// stay; anything else propagates. Each app id is resolved once per run, so a redraw cannot retry it. Thread-safe.
/// </summary>
public sealed class AppIconSource
{
    private const int StoredSize = 64;
    private const string SteamRepairKey = "icons.steamRepaired2"; // "icons.steamRepaired" was the first pass, which used the exe icon alone

    private readonly string _dbPath;
    private readonly IDesktop _desktop;
    private readonly object _gate = new();
    private readonly Dictionary<long, byte[]> _found = new();
    private readonly HashSet<long> _tried = new();
    private readonly Dictionary<(string Path, int Size), byte[]?> _byPath = new();
    private bool _steamChecked; // this run has seen the Steam repair done (under _gate)

    public AppIconSource(string dbPath, IDesktop desktop)
    {
        _dbPath = dbPath;
        _desktop = desktop;
    }

    /// <summary>
    /// The icons of the given apps; an app with none is left out. The first request for an id reads the database and
    /// extracts a missing icon (call from a pool thread); later requests answer from memory.
    /// </summary>
    public IReadOnlyDictionary<long, byte[]> ForApps(IEnumerable<long> appIds)
    {
        var ids = appIds.Distinct().ToList();
        List<long> want;
        lock (_gate) want = ids.Where(id => !_found.ContainsKey(id) && _tried.Add(id)).ToList();
        if (want.Count > 0) Resolve(want);
        lock (_gate) return ids.Where(_found.ContainsKey).ToDictionary(id => id, id => _found[id]);
    }

    /// <summary>
    /// The icon of an app by exe path at about <paramref name="size"/> px, extracted once per path and size (a failed
    /// extraction is remembered as no icon). <paramref name="aumid"/> and <paramref name="imageFile"/> help find the logo
    /// of a packaged app or Steam game (a Steam exe gets its cached art looked up when none is given) and do not change
    /// the cache key. With <paramref name="cachedOnly"/> nothing is
    /// extracted and an unknown path gives null. <paramref name="extracted"/> says whether this call ran an extraction.
    /// </summary>
    public byte[]? ForPath(string exePath, string? aumid, string? imageFile, int size, bool cachedOnly, out bool extracted)
    {
        extracted = false;
        var key = (exePath.ToLowerInvariant(), size);
        lock (_gate)
        {
            if (_byPath.TryGetValue(key, out var known)) return known;
        }
        if (cachedOnly) return null;
        if (imageFile is null && SteamLibrary.IsInLibrary(exePath)) imageFile = SteamLibrary.ArtForExe(exePath);
        byte[]? png;
        try { png = _desktop.ExtractIcon(exePath, aumid, imageFile, size); }
        catch (IOException) { png = null; }
        extracted = true;
        lock (_gate) _byPath[key] = png;
        return png;
    }

    /// <summary>The stored icons of the given apps (a Merged app too); an app with none stored is left out. Never extracts.</summary>
    public IReadOnlyDictionary<long, byte[]> Stored(IEnumerable<long> appIds)
    {
        var ids = appIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, byte[]>();
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            return lib.GetIcons(ids);
        }
        catch (Exception e) when (AppCatalogue.IsBusyOrMissing(e)) { return new Dictionary<long, byte[]>(); }
    }

    private void Resolve(IReadOnlyList<long> want)
    {
        var pngs = new Dictionary<long, byte[]>();
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            IReadOnlyList<LibraryApp>? apps = null;
            RepairSteamIcons(lib, ref apps);
            byte[]? own = null;
            foreach (var id in want)
            {
                apps ??= lib.Library();
                var app = apps.FirstOrDefault(a => a.Id == id);
                var png = lib.GetIcon(id);
                if (app is not null && IsSelf(app.ExePath))
                {
                    // TimeSink's own row shows the chosen app icon; an exe-extracted copy can be a stale generic one.
                    own ??= IconCatalogue.ReadFrame(IconCatalogue.Resolve(SettingsStore.Get(_dbPath, "ui.icon")), StoredSize);
                    if (!own.AsSpan().SequenceEqual(png)) lib.PutIcon(id, own);
                    png = own;
                }
                else if (png is null && app is not null && Extract(app.ExePath, SteamLibrary.ArtForExe(app.ExePath)) is { } extracted)
                {
                    lib.PutIcon(id, extracted);
                    png = extracted;
                }
                if (png is not null) pngs[id] = png;
            }
        }
        catch (Exception e) when (AppCatalogue.IsBusyOrMissing(e)) { } // no database yet: letters stay
        lock (_gate)
            foreach (var (id, png) in pngs) _found[id] = png;
    }

    /// <summary>
    /// Steam games used to be stored as their 32 px community icon scaled up. Once per database, the first time any app
    /// is resolved, every stored Steam exe icon is extracted again by the current rule (the exe's icon, or Steam's art
    /// when the exe's own icon is tiny or unreadable); an app that gets nothing keeps its icon, and the flag is set
    /// anyway so it does not run again.
    /// </summary>
    private void RepairSteamIcons(AppLibrary lib, ref IReadOnlyList<LibraryApp>? apps)
    {
        lock (_gate)
        {
            if (_steamChecked) return;
        }
        if (SettingsStore.Get(_dbPath, SteamRepairKey) != "1")
        {
            apps ??= lib.Library();
            foreach (var app in apps.Where(a => SteamLibrary.IsInLibrary(a.ExePath) && !IsSelf(a.ExePath)))
                if (Extract(app.ExePath, SteamLibrary.ArtForExe(app.ExePath)) is { } png) lib.PutIcon(app.Id, png);
            SettingsStore.Set(_dbPath, SteamRepairKey, "1");
        }
        lock (_gate) _steamChecked = true;
    }

    private byte[]? Extract(string exePath, string? imageFile)
    {
        try { return _desktop.ExtractIcon(exePath, null, imageFile, StoredSize); }
        catch (IOException) { return null; }
    }

    private static bool IsSelf(string exePath) =>
        Path.GetFileName(exePath).StartsWith("TimeSink.", StringComparison.OrdinalIgnoreCase);
}
