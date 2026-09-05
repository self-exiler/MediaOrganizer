using MediaOrganizer.Core.Storage;

namespace MediaOrganizer.Core.Tests;

public class WebDavAddressTests
{
    [Fact]
    public void 剔除零宽字符且中文路径保持()
    {
        var result = WebDavAddress.Sanitize("http://132.145.99.231:5244\u200B/dav/onedrive-diohamilton/图片");
        var uri = new Uri(result);
        Assert.Equal("132.145.99.231", uri.Host);
        Assert.Equal(5244, uri.Port);
        Assert.Contains("%E5%9B%BE%E7%89%87", uri.AbsolutePath);
    }

    [Fact]
    public void 剔除BOM前缀()
    {
        var result = WebDavAddress.Sanitize("\uFEFFhttps://dav.example.com/photos");
        Assert.StartsWith("https://dav.example.com/photos", result);
    }

    [Fact]
    public void 主机段全角数字与冒号转半角()
    {
        var result = WebDavAddress.Sanitize("http://１３２.１４５.９９.２３１：5244/dav/图片");
        var uri = new Uri(result);
        Assert.Equal("132.145.99.231", uri.Host);
        Assert.Equal(5244, uri.Port);
    }

    [Fact]
    public void 路径全角斜杠转半角()
    {
        var result = WebDavAddress.Sanitize("http://1.2.3.4:5244／dav／图片");
        Assert.Equal("/dav/%E5%9B%BE%E7%89%87", new Uri(result).AbsolutePath);
    }

    [Fact]
    public void 缺省协议补http()
    {
        Assert.StartsWith("http://132.145.99.231:5244/dav", WebDavAddress.Sanitize("132.145.99.231:5244/dav"));
    }

    [Fact]
    public void 全角scheme归一()
    {
        var result = WebDavAddress.Sanitize("ｈｔｔｐ：／／1.2.3.4/dav");
        Assert.StartsWith("http://1.2.3.4/dav", result);
    }

    [Fact]
    public void 干净地址保持规范形态()
    {
        Assert.Equal("https://dav.jianguoyun.com/dav/", WebDavAddress.Sanitize("https://dav.jianguoyun.com/dav/"));
    }

    [Fact]
    public void 空地址抛异常()
    {
        Assert.Throws<ArgumentException>(() => WebDavAddress.Sanitize(""));
        Assert.Throws<ArgumentException>(() => WebDavAddress.Sanitize("   "));
    }

    [Fact]
    public void 含污染字符的地址可构造WebDavFileStorage()
    {
        // 旧配置防御：构造器内部清洗，不应抛出
        using var storage = new WebDavFileStorage("http://\uFEFF132.145.99.231:5244\u200B/dav/图片", "u", "p");
        Assert.NotNull(storage);
    }
}
