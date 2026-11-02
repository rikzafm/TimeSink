using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Threading;

namespace TimeSink.Core.Security;

/// <summary>
/// Windows DPAPI-based implementation of <see cref="IKeyManager"/>.
/// Encrypts and decrypts a 256-bit database key bound to the current Windows user.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyManager : IKeyManager
{
    public const string DefaultFileName = ".dbkey";
    public const string DefaultSyncKeyFileName = ".synckey";

    private static byte[]? _cachedDefaultKey;
    private static string? _cachedDefaultSyncPassphrase;
    private static DateTime? _cachedDefaultSyncPassphraseLastWriteUtc;
    private static readonly object StaticLock = new();

    private readonly string _keyFilePath;
    private readonly string _syncKeyFilePath;
    private byte[]? _cachedKey;
    private string? _cachedSyncPassphrase;
    private DateTime? _cachedSyncPassphraseLastWriteUtc;
    private readonly object _instanceLock = new();

    public DpapiKeyManager(string? keyFilePath = null, string? syncKeyFilePath = null)
    {
        _keyFilePath = keyFilePath ?? GetDefaultKeyFilePath();
        _syncKeyFilePath = syncKeyFilePath ?? (keyFilePath is not null
            ? Path.Combine(Path.GetDirectoryName(keyFilePath) ?? "", DefaultSyncKeyFileName)
            : GetDefaultSyncKeyFilePath());
    }

    public static string GetDefaultKeyFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TimeSink", DefaultFileName);
    }

    public static string GetDefaultSyncKeyFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TimeSink", DefaultSyncKeyFileName);
    }

    public string KeyFilePath => _keyFilePath;
    public string SyncKeyFilePath => _syncKeyFilePath;

    public byte[] GetOrCreateDatabaseKey()
    {
        lock (_instanceLock)
        {
            if (_cachedKey is not null) return (byte[])_cachedKey.Clone();
        }

        var isDefault = string.Equals(_keyFilePath, GetDefaultKeyFilePath(), StringComparison.OrdinalIgnoreCase);
        if (isDefault)
        {
            lock (StaticLock)
            {
                if (_cachedDefaultKey is not null)
                {
                    _cachedKey = (byte[])_cachedDefaultKey.Clone();
                    return (byte[])_cachedKey.Clone();
                }
            }
        }

        // Cross-process and cross-thread synchronization for key creation/reading
        var mutexName = @"Local\TimeSink_DbKey_Lock_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_keyFilePath)));
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;
        try
        {
            try { hasHandle = mutex.WaitOne(5000); } catch (AbandonedMutexException) { hasHandle = true; }

            if (File.Exists(_keyFilePath))
            {
                byte[] encryptedBytes;
                using (var fs = new FileStream(_keyFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    encryptedBytes = new byte[fs.Length];
                    var totalRead = 0;
                    while (totalRead < encryptedBytes.Length)
                    {
                        var read = fs.Read(encryptedBytes, totalRead, encryptedBytes.Length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                }

                var key = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                _cachedKey = (byte[])key.Clone();
                if (isDefault)
                {
                    lock (StaticLock) { _cachedDefaultKey = (byte[])key.Clone(); }
                }
                return key;
            }

            var newKey = RandomNumberGenerator.GetBytes(32);
            var encrypted = ProtectedData.Protect(newKey, null, DataProtectionScope.CurrentUser);

            var dir = Path.GetDirectoryName(_keyFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempFile = _keyFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(encrypted);
                fs.Flush();
            }

            File.Move(tempFile, _keyFilePath, overwrite: true);

            _cachedKey = (byte[])newKey.Clone();
            if (isDefault)
            {
                lock (StaticLock) { _cachedDefaultKey = (byte[])newKey.Clone(); }
            }
            return newKey;
        }
        finally
        {
            if (hasHandle) mutex.ReleaseMutex();
        }
    }

    public void DeleteDatabaseKey()
    {
        lock (_instanceLock) { _cachedKey = null; }
        if (string.Equals(_keyFilePath, GetDefaultKeyFilePath(), StringComparison.OrdinalIgnoreCase))
        {
            lock (StaticLock) { _cachedDefaultKey = null; }
        }

        var mutexName = @"Local\TimeSink_DbKey_Lock_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_keyFilePath)));
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;
        try
        {
            try { hasHandle = mutex.WaitOne(5000); } catch (AbandonedMutexException) { hasHandle = true; }
            if (File.Exists(_keyFilePath))
            {
                File.Delete(_keyFilePath);
            }
        }
        finally
        {
            if (hasHandle) mutex.ReleaseMutex();
        }
    }

    public bool HasSyncPassphrase()
    {
        var isDefault = string.Equals(_syncKeyFilePath, GetDefaultSyncKeyFilePath(), StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(_syncKeyFilePath))
        {
            lock (_instanceLock)
            {
                _cachedSyncPassphrase = null;
                _cachedSyncPassphraseLastWriteUtc = null;
            }

            if (isDefault)
            {
                lock (StaticLock)
                {
                    _cachedDefaultSyncPassphrase = null;
                    _cachedDefaultSyncPassphraseLastWriteUtc = null;
                }
            }

            return false;
        }

        var diskWriteUtc = File.GetLastWriteTimeUtc(_syncKeyFilePath);
        lock (_instanceLock)
        {
            if (_cachedSyncPassphraseLastWriteUtc is not null && _cachedSyncPassphraseLastWriteUtc != diskWriteUtc)
            {
                _cachedSyncPassphrase = null;
                _cachedSyncPassphraseLastWriteUtc = null;
            }
        }

        if (isDefault)
        {
            lock (StaticLock)
            {
                if (_cachedDefaultSyncPassphraseLastWriteUtc is not null && _cachedDefaultSyncPassphraseLastWriteUtc != diskWriteUtc)
                {
                    _cachedDefaultSyncPassphrase = null;
                    _cachedDefaultSyncPassphraseLastWriteUtc = null;
                }
            }
        }

        return true;
    }

    public string? GetSyncPassphrase()
    {
        var isDefault = string.Equals(_syncKeyFilePath, GetDefaultSyncKeyFilePath(), StringComparison.OrdinalIgnoreCase);

        if (!File.Exists(_syncKeyFilePath))
        {
            lock (_instanceLock)
            {
                _cachedSyncPassphrase = null;
                _cachedSyncPassphraseLastWriteUtc = null;
            }

            if (isDefault)
            {
                lock (StaticLock)
                {
                    _cachedDefaultSyncPassphrase = null;
                    _cachedDefaultSyncPassphraseLastWriteUtc = null;
                }
            }

            return null;
        }

        var diskWriteUtc = File.GetLastWriteTimeUtc(_syncKeyFilePath);

        lock (_instanceLock)
        {
            if (_cachedSyncPassphrase is not null && _cachedSyncPassphraseLastWriteUtc == diskWriteUtc)
            {
                return _cachedSyncPassphrase;
            }
        }

        if (isDefault)
        {
            lock (StaticLock)
            {
                if (_cachedDefaultSyncPassphrase is not null && _cachedDefaultSyncPassphraseLastWriteUtc == diskWriteUtc)
                {
                    lock (_instanceLock)
                    {
                        _cachedSyncPassphrase = _cachedDefaultSyncPassphrase;
                        _cachedSyncPassphraseLastWriteUtc = diskWriteUtc;
                    }
                    return _cachedSyncPassphrase;
                }
            }
        }

        var mutexName = @"Local\TimeSink_SyncKey_Lock_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_syncKeyFilePath)));
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;
        try
        {
            try { hasHandle = mutex.WaitOne(5000); } catch (AbandonedMutexException) { hasHandle = true; }

            if (!File.Exists(_syncKeyFilePath))
            {
                lock (_instanceLock)
                {
                    _cachedSyncPassphrase = null;
                    _cachedSyncPassphraseLastWriteUtc = null;
                }
                if (isDefault)
                {
                    lock (StaticLock)
                    {
                        _cachedDefaultSyncPassphrase = null;
                        _cachedDefaultSyncPassphraseLastWriteUtc = null;
                    }
                }
                return null;
            }

            var currentWriteUtc = File.GetLastWriteTimeUtc(_syncKeyFilePath);

            lock (_instanceLock)
            {
                if (_cachedSyncPassphrase is not null && _cachedSyncPassphraseLastWriteUtc == currentWriteUtc)
                {
                    return _cachedSyncPassphrase;
                }
            }

            if (isDefault)
            {
                lock (StaticLock)
                {
                    if (_cachedDefaultSyncPassphrase is not null && _cachedDefaultSyncPassphraseLastWriteUtc == currentWriteUtc)
                    {
                        lock (_instanceLock)
                        {
                            _cachedSyncPassphrase = _cachedDefaultSyncPassphrase;
                            _cachedSyncPassphraseLastWriteUtc = currentWriteUtc;
                        }
                        return _cachedSyncPassphrase;
                    }
                }
            }

            byte[] encryptedBytes;
            using (var fs = new FileStream(_syncKeyFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                encryptedBytes = new byte[fs.Length];
                var totalRead = 0;
                while (totalRead < encryptedBytes.Length)
                {
                    var read = fs.Read(encryptedBytes, totalRead, encryptedBytes.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
            }

            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var passphrase = System.Text.Encoding.UTF8.GetString(plainBytes);
            var fileWriteTimeUtc = File.GetLastWriteTimeUtc(_syncKeyFilePath);

            lock (_instanceLock)
            {
                _cachedSyncPassphrase = passphrase;
                _cachedSyncPassphraseLastWriteUtc = fileWriteTimeUtc;
            }

            if (isDefault)
            {
                lock (StaticLock)
                {
                    _cachedDefaultSyncPassphrase = passphrase;
                    _cachedDefaultSyncPassphraseLastWriteUtc = fileWriteTimeUtc;
                }
            }

            return passphrase;
        }
        finally
        {
            if (hasHandle) mutex.ReleaseMutex();
        }
    }

    public void SetSyncPassphrase(string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        var isDefault = string.Equals(_syncKeyFilePath, GetDefaultSyncKeyFilePath(), StringComparison.OrdinalIgnoreCase);
        var mutexName = @"Local\TimeSink_SyncKey_Lock_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_syncKeyFilePath)));
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;
        try
        {
            try { hasHandle = mutex.WaitOne(5000); } catch (AbandonedMutexException) { hasHandle = true; }

            var plainBytes = System.Text.Encoding.UTF8.GetBytes(passphrase);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

            var dir = Path.GetDirectoryName(_syncKeyFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempFile = _syncKeyFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(encrypted);
                fs.Flush();
            }

            File.Move(tempFile, _syncKeyFilePath, overwrite: true);
            var writeTimeUtc = File.GetLastWriteTimeUtc(_syncKeyFilePath);

            lock (_instanceLock)
            {
                _cachedSyncPassphrase = passphrase;
                _cachedSyncPassphraseLastWriteUtc = writeTimeUtc;
            }

            if (isDefault)
            {
                lock (StaticLock)
                {
                    _cachedDefaultSyncPassphrase = passphrase;
                    _cachedDefaultSyncPassphraseLastWriteUtc = writeTimeUtc;
                }
            }
        }
        finally
        {
            if (hasHandle) mutex.ReleaseMutex();
        }
    }

    public void ClearSyncPassphrase()
    {
        lock (_instanceLock)
        {
            _cachedSyncPassphrase = null;
            _cachedSyncPassphraseLastWriteUtc = null;
        }

        var isDefault = string.Equals(_syncKeyFilePath, GetDefaultSyncKeyFilePath(), StringComparison.OrdinalIgnoreCase);
        if (isDefault)
        {
            lock (StaticLock)
            {
                _cachedDefaultSyncPassphrase = null;
                _cachedDefaultSyncPassphraseLastWriteUtc = null;
            }
        }

        var mutexName = @"Local\TimeSink_SyncKey_Lock_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(_syncKeyFilePath)));
        using var mutex = new Mutex(false, mutexName);
        var hasHandle = false;
        try
        {
            try { hasHandle = mutex.WaitOne(5000); } catch (AbandonedMutexException) { hasHandle = true; }

            if (File.Exists(_syncKeyFilePath))
            {
                File.Delete(_syncKeyFilePath);
            }
        }
        finally
        {
            if (hasHandle) mutex.ReleaseMutex();
        }
    }
}
