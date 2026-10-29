using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Services;

/// <summary>
/// 音乐目录体系（用户需求：上传音频统一落盘 music 根，共享在根、私有在 /{昵称}/）：
/// 根目录 = SystemConfig player.musicDir（admin 可指外部盘），未配置默认 {data}/music。
/// MediaAsset.Subdir 存标记（"@music" 共享 / "@music/{昵称}" 私有 / null 老位置），
/// 运行时解析为绝对路径——换配置/换机不漂移。
/// </summary>
public static class MusicDir
{
    public const string ConfigKey = "player.musicDir";
    public const string Marker = "@music";

    /// <summary>解析音乐根目录（绝对路径）：配置优先，否则 {data}/music。</summary>
    public static async Task<string> ResolveRootAsync(HZoneDbContext db, CancellationToken ct)
    {
        var configured = await db.SystemConfigs.AsNoTracking()
            .Where(c => c.Key == ConfigKey)
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }
        return LocalFileStorage.DefaultMusicDir("data");
    }

    /// <summary>共享音频子目录标记。</summary>
    public static string SharedMarker => Marker;

    /// <summary>私有音频子目录标记（昵称 sanitize 后做目录名）。</summary>
    public static string PrivateMarker(string nickname) => $"{Marker}/{Sanitize(nickname)}";

    /// <summary>Subdir 标记 → 绝对路径；null（老资产）返回 null（= data/media 老位置）。</summary>
    public static string? ToAbsolute(string? subdir, string musicRoot) =>
        subdir is null ? null
        : subdir == Marker ? musicRoot
        : subdir.StartsWith(Marker + "/", StringComparison.Ordinal)
            ? Path.Combine(musicRoot, subdir[(Marker.Length + 1)..])
        : null;

    /// <summary>昵称 → 安全目录名（Windows 非法字符替换，空回落 user）。</summary>
    public static string Sanitize(string nickname)
    {
        var invalid = new HashSet<char>("\\/:*?\"<>|".ToCharArray());
        var cleaned = new string(nickname.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "user" : cleaned[..Math.Min(40, cleaned.Length)];
    }
}
