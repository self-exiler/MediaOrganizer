using MediaOrganizer.Core;
using MediaOrganizer.Core.Configuration;
using MediaOrganizer.Core.Security;
using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

public class CredentialCryptoTests
{
    [Fact]
    public void 加密后不包含明文()
    {
        var encrypted = CredentialCrypto.Encrypt("P@ssw0rd123");
        Assert.NotEqual("P@ssw0rd123", encrypted);
        Assert.DoesNotContain("P@ssw0rd123", encrypted);
    }

    [Fact]
    public void 解密还原()
    {
        var encrypted = CredentialCrypto.Encrypt("my-secret");
        Assert.Equal("my-secret", CredentialCrypto.Decrypt(encrypted));
    }

    [Fact]
    public void 空串原样返回()
    {
        Assert.Equal("", CredentialCrypto.Encrypt(""));
        Assert.Equal("", CredentialCrypto.Decrypt(""));
    }

    [Fact]
    public void 兼容旧明文()
    {
        Assert.Equal("legacy", CredentialCrypto.Decrypt("legacy"));
    }

    [Fact]
    public void Windows下使用强加密前缀()
    {
        var encrypted = CredentialCrypto.Encrypt("x");
        if (OperatingSystem.IsWindows())
            Assert.StartsWith(CredentialCrypto.DpapiPrefix, encrypted);
        else
            Assert.StartsWith(CredentialCrypto.B64Prefix, encrypted);
    }
}

public class StorageFactoryTests
{
    [Fact]
    public void 未选网络位置时用本地存储()
    {
        var config = new AppConfig { Paths = new PathsConfig { OutputDir = @"D:\out" } };
        var storage = StorageFactory.CreateTarget(config, profileName: null);
        Assert.IsType<LocalFileStorage>(storage);
    }

    [Fact]
    public void 选中SMB配置时用Smb存储()
    {
        var config = new AppConfig();
        config.NetworkProfiles.Add(new NetworkProfile
        {
            Name = "nas", Type = NetworkType.Smb, Address = @"\\192.168.1.10\photos",
            Username = "u", Password = CredentialCrypto.Encrypt("p")
        });
        var storage = StorageFactory.CreateTarget(config, "nas");
        Assert.IsType<SmbFileStorage>(storage);
    }

    [Fact]
    public void 选中WebDAV配置时用WebDav存储()
    {
        var config = new AppConfig();
        config.NetworkProfiles.Add(new NetworkProfile
        {
            Name = "wd", Type = NetworkType.WebDav, Address = "https://dav.example.com/photos",
            Username = "u", Password = CredentialCrypto.Encrypt("p")
        });
        var storage = StorageFactory.CreateTarget(config, "wd");
        Assert.IsType<WebDavFileStorage>(storage);
    }
}
