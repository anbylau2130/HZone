using System.Text.RegularExpressions;
using HZone.Core.Interfaces;

namespace HZone.Api.Services;

/// <summary>
/// 本地磁盘媒体存储实现。storedName = {guid}{ext}，扩展名从 Content-Type 映射。
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private static readonly Regex ValidStoredName = new("^[A-Za-z0-9._-]{1,128}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
        ["video/mp4"] = ".mp4",
        ["video/webm"] = ".webm",
        ["video/quicktime"] = ".mov",
        ["video/x-msvideo"] = ".avi",
        ["audio/mpeg"] = ".mp3",
        ["audio/mp4"] = ".m4a",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
        ["audio/ogg"] = ".ogg",
    };

    private readonly string _mediaDirectory;

    public LocalFileStorage(IConfiguration configuration)
    {
        var dataDirectory = configuration["HZone:DataDirectory"] ?? "data";
        _mediaDirectory = Path.Combine(dataDirectory, "media");
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_mediaDirectory);
        var storedName = $"{Guid.NewGuid():N}{ResolveExtension(contentType)}";
        var path = ResolvePath(storedName);
        await using var target = File.Create(path);
        await content.CopyToAsync(target, ct);
        return storedName;
    }

    public Task DeleteAsync(string storedName, CancellationToken ct = default)
    {
        var path = ResolvePath(storedName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> OpenAsync(string storedName)
    {
        return Task.FromResult<Stream>(File.OpenRead(ResolvePath(storedName)));
    }

    private string ResolvePath(string storedName)
    {
        if (!ValidStoredName.IsMatch(storedName) || storedName.Contains(".."))
        {
            throw new ArgumentException("invalid stored name", nameof(storedName));
        }

        return Path.GetFullPath(Path.Combine(_mediaDirectory, storedName));
    }

    private static string ResolveExtension(string contentType)
    {
        return ExtensionByContentType.TryGetValue(contentType, out var extension) ? extension : ".bin";
    }
}
