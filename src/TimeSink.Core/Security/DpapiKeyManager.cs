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
    private static byte[]? _cachedDefaultKey;
    private static readonly object StaticLock = new();

    private readonly string _keyFilePath;
    private byte[]? _cachedKey;
    private readonly object _instanceLock = new();

    public DpapiKeyManager(string? keyFilePath = null)
    {
        _keyFilePath = keyFilePath ?? GetDefaultKeyFilePath();
    }

    public static string GetDefaultKeyFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "TimeSink", DefaultFileName);
    }

    public string KeyFilePath => _keyFilePath;

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
}
