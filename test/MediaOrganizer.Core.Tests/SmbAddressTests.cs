using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

public class SmbAddressTests
{
    [Theory]
    [InlineData(@"\\192.168.1.10\photos", "192.168.1.10", "photos", "")]
    [InlineData(@"\\NAS\media\2024", "NAS", "media", "2024")]
    [InlineData(@"\\server\share\a\b\c", "server", "share", @"a\b\c")]
    [InlineData("192.168.1.10/photos", "192.168.1.10", "photos", "")]
    [InlineData("nas/media/2024", "nas", "media", "2024")]
    [InlineData(@"\\192.168.1.10\photos\", "192.168.1.10", "photos", "")]
    public void 解析标准地址(string input, string server, string share, string path)
    {
        var r = SmbFileStorage.ParseAddress(input);
        Assert.Equal(server, r.Server);
        Assert.Equal(share, r.Share);
        Assert.Equal(path, r.Path);
    }

    [Theory]
    [InlineData(@"\\only-server")]
    [InlineData("noslash")]
    [InlineData("")]
    public void 缺少共享名时抛出带提示的异常(string input)
    {
        var ex = Assert.Throws<ArgumentException>(() => SmbFileStorage.ParseAddress(input));
        Assert.Contains("SMB 地址格式无效", ex.Message);
    }
}
