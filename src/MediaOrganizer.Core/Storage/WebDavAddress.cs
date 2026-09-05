using System.Text;

namespace MediaOrganizer.Core.Storage;

/// <summary>
/// WebDAV 地址清洗与校验（FR-10 / FR-A8.3）：手机输入法/剪贴板输入常混入不可见字符
/// （零宽字符、BOM、双向控制符）或全角字符（／ ： ．、全角数字），Trim 无法去除，
/// 一旦粘在主机段就会导致 DNS 解析失败（“hostname nor servename provided, or not known”）。
/// 清洗规则：全串剔除不可见字符；authority（scheme://host:port）段全角转半角；
/// 路径段保留原样（中文目录名合法，仅全角斜杠转半角）；最终以 Uri 重建规范形态（路径自动百分号编码）。
/// </summary>
public static class WebDavAddress
{
    /// <summary>零宽字符、BOM、软连字符、双向控制符——输入法/剪贴板常见不可见混入。</summary>
    private static readonly char[] InvisibleChars =
    [
        '\u200B', '\u200C', '\u200D', '\u2060', '\uFEFF', '\u00AD',
        '\u202A', '\u202B', '\u202C', '\u202D', '\u202E',
        '\u2066', '\u2067', '\u2068', '\u2069'
    ];

    /// <summary>清洗 + 校验，返回规范形态地址；格式非法抛 <see cref="ArgumentException"/>（消息可直接展示给用户）。</summary>
    public static string Sanitize(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("WebDAV 地址不能为空", nameof(address));

        var text = StripInvisible(address).Trim().Trim('\u00A0', '\u3000').Trim();

        // scheme 归一（兼容全角 ｈｔｔｐ：／／）；缺失时补 http://（手机输入常态）
        var head = ToAsciiHalfWidth(text.Length <= 12 ? text : text[..12]).ToLowerInvariant();
        string scheme, rest;
        var delimIdx = head.IndexOf("://", StringComparison.Ordinal);
        if (delimIdx >= 0 && head.StartsWith("https://", StringComparison.Ordinal))
        {
            scheme = "https";
            rest = text[(delimIdx + 3)..];
        }
        else if (delimIdx >= 0 && head.StartsWith("http://", StringComparison.Ordinal))
        {
            scheme = "http";
            rest = text[(delimIdx + 3)..];
        }
        else
        {
            scheme = "http";
            rest = text;
        }

        // authority 段（主机/端口/账号）集中了输入污染，全角转半角；路径保留原样（仅全角斜杠转半角）
        var pathStart = -1;
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i] is '/' or '／')
            {
                pathStart = i;
                break;
            }
        }

        var authority = ToAsciiHalfWidth(pathStart < 0 ? rest : rest[..pathStart]);
        var path = pathStart < 0 ? "/" : rest[pathStart..].Replace('／', '/');
        if (!path.StartsWith('/')) path = "/" + path;

        if (!Uri.TryCreate($"{scheme}://{authority}{path}", UriKind.Absolute, out var uri)
            || uri.HostNameType == UriHostNameType.Unknown)
        {
            throw new ArgumentException($"WebDAV 地址格式无效：{address}（应为 http(s)://服务器:端口/路径）");
        }

        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath + uri.Query;
    }

    private static string StripInvisible(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (Array.IndexOf(InvisibleChars, ch) < 0) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>全角 ASCII 变体（U+FF01..U+FF5E）转半角；中文等其余字符原样保留。</summary>
    private static string ToAsciiHalfWidth(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch is >= '\uFF01' and <= '\uFF5E' ? (char)(ch - 0xFEE0) : ch);
        }
        return sb.ToString();
    }
}
