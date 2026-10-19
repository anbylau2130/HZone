using System.Text.RegularExpressions;

namespace HZone.Tests.Helpers;

public static class EmlHelper
{
    /// <summary>
    /// 读取 factory DataDirectory/outbox 下最新的一封 .eml，从正文中提取链接 token。
    /// </summary>
    public static async Task<string> ExtractLatestTokenAsync(string dataDirectory, string linkFragment)
    {
        var outbox = Path.Combine(dataDirectory, "outbox");
        var latest = new DirectoryInfo(outbox).GetFiles("*.eml")
            .OrderByDescending(f => f.Name)
            .First();
        var content = await File.ReadAllTextAsync(latest.FullName);
        var match = Regex.Match(content, Regex.Escape(linkFragment) + @"\?token=([A-Za-z0-9]+)");
        Assert.True(match.Success, $"no token found for '{linkFragment}' in {latest.Name}");
        return match.Groups[1].Value;
    }
}
