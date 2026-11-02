using System.Runtime.Versioning;
using System.Security.Cryptography;
using TimeSink.Core.Security;

namespace TimeSink.Tests.Security;

[SupportedOSPlatform("windows")]
public sealed class DpapiKeyManagerTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _keyPath;

    public DpapiKeyManagerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "timesink-dpapi-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _keyPath = Path.Combine(_testDir, ".dbkey");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public void GetOrCreateDatabaseKey_generates_32_byte_key()
    {
        var manager = new DpapiKeyManager(_keyPath);
        var key = manager.GetOrCreateDatabaseKey();

        key.Should().NotBeNull();
        key.Length.Should().Be(32);
        File.Exists(_keyPath).Should().BeTrue();
    }

    [Fact]
    public void GetOrCreateDatabaseKey_is_idempotent()
    {
        var manager1 = new DpapiKeyManager(_keyPath);
        var key1 = manager1.GetOrCreateDatabaseKey();

        // A second instance pointing to the same file should retrieve identical key
        var manager2 = new DpapiKeyManager(_keyPath);
        var key2 = manager2.GetOrCreateDatabaseKey();

        key1.Should().Equal(key2);
    }

    [Fact]
    public void DatabaseKey_file_is_dpapi_protected_not_plaintext()
    {
        var manager = new DpapiKeyManager(_keyPath);
        var key = manager.GetOrCreateDatabaseKey();

        var rawFileBytes = File.ReadAllBytes(_keyPath);
        // The raw file bytes must NOT be equal to the plaintext key bytes
        rawFileBytes.Should().NotEqual(key);

        // Decrypting the raw bytes with DPAPI CurrentUser should return the key
        var unprotected = ProtectedData.Unprotect(rawFileBytes, null, DataProtectionScope.CurrentUser);
        unprotected.Should().Equal(key);
    }

    [Fact]
    public void DeleteDatabaseKey_removes_key_file()
    {
        var manager = new DpapiKeyManager(_keyPath);
        _ = manager.GetOrCreateDatabaseKey();
        File.Exists(_keyPath).Should().BeTrue();

        manager.DeleteDatabaseKey();
        File.Exists(_keyPath).Should().BeFalse();
    }
}
