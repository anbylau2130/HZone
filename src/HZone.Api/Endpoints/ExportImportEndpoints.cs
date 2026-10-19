using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Core.Interfaces;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 整站导出/导入（ticket 10，PRD 6.7）：导出即备份，导入即恢复（默认私密）。
/// zip 结构：journals/*.md（frontmatter）、says/*.txt（文本 + 媒体清单）、media/*、manifest.json。
/// </summary>
public static class ExportImportEndpoints
{
    /// <summary>zip 条目时间戳固定（zip 格式最早合法时间），导出产物可复现。</summary>
    private static readonly DateTimeOffset FixedEntryTime =
        new(new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>说说文本与媒体清单的分隔行（含完整说说的媒体文件名列表）。</summary>
    private const string MediaMarker = "--hzone-media--";

    /// <summary>媒体存储名规则与 LocalFileStorage 一致（防路径穿越）。</summary>
    private static readonly System.Text.RegularExpressions.Regex ValidStoredName =
        new("^[A-Za-z0-9._-]{1,128}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static void MapExportImportEndpoints(this IEndpointRouteBuilder app)
    {
        // 导出调用者的全部内容（自己的说说 + 日志 + 媒体）；可见名单仅记昵称。
        app.MapGet("/export", [Authorize] async (
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);

            var contents = await db.Contents
                .Where(c => c.AuthorId == me)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct);
            var contentIds = contents.Select(c => c.Id).ToList();
            var recipientsByContent = (await db.ContentRecipients
                    .Where(r => contentIds.Contains(r.ContentId))
                    .Select(r => new { r.ContentId, r.User.Nickname })
                    .ToListAsync(ct))
                .GroupBy(r => r.ContentId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.Nickname).ToList());

            var says = contents.OfType<Say>().ToList();
            var sayIds = says.Select(s => s.Id).ToList();
            var mediaAssets = sayIds.Count == 0
                ? new List<MediaAsset>()
                : await db.MediaAssets
                    .Where(m => m.ContentId != null && sayIds.Contains(m.ContentId.Value))
                    .ToListAsync(ct);
            var mediaBySay = mediaAssets
                .GroupBy(m => m.ContentId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            // 内存组包（小圈子规模可接受）；不能 using：响应写完由 Results.File 负责 dispose。
            var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var journal in contents.OfType<Journal>())
                {
                    var recipients = recipientsByContent.GetValueOrDefault(journal.Id, new List<string>());
                    var entry = zip.CreateEntry($"journals/{SafeFileName(journal.Title)}-{ShortId(journal.Id)}.md");
                    entry.LastWriteTime = FixedEntryTime;
                    await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    await writer.WriteAsync(BuildJournalMarkdown(journal, recipients));
                }

                foreach (var say in says)
                {
                    var media = mediaBySay.GetValueOrDefault(say.Id, new List<MediaAsset>());
                    var entry = zip.CreateEntry($"says/{ShortId(say.Id)}.txt");
                    entry.LastWriteTime = FixedEntryTime;
                    await using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    await writer.WriteAsync(BuildSayText(say, media));
                }

                foreach (var asset in mediaAssets)
                {
                    var entry = zip.CreateEntry($"media/{asset.StoredName}");
                    entry.LastWriteTime = FixedEntryTime;
                    await using var entryStream = entry.Open();
                    await using var fileStream = await storage.OpenAsync(asset.StoredName);
                    await fileStream.CopyToAsync(entryStream, ct);
                }

                var manifestEntry = zip.CreateEntry("manifest.json");
                manifestEntry.LastWriteTime = FixedEntryTime;
                await using var manifestWriter = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false));
                await manifestWriter.WriteAsync(JsonSerializer.Serialize(new
                {
                    version = 1,
                    exportedAt = DateTime.UtcNow,
                    counts = new
                    {
                        journals = contents.Count(c => c is Journal),
                        says = says.Count,
                        media = mediaAssets.Count,
                    },
                    media = mediaAssets.Select(m => new
                    {
                        m.StoredName,
                        kind = m.Kind.ToString().ToLowerInvariant(),
                        m.ContentType,
                        m.OriginalName,
                        m.SizeBytes,
                    }),
                }));
            }

            buffer.Position = 0;
            return Results.File(buffer, "application/zip", $"hzone-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip");
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 导入 zip：日志保留 createdAt；内容一律默认私密（不建 ContentRecipient，PRD Q23）；
        // 说说重建文本 + 重新落盘媒体（storedName 重新生成避免冲突）；不做去重（恢复场景 = 清库后导入）。
        app.MapPost("/import", [Authorize] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "multipart form with 'file' field required" });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null)
            {
                return Results.BadRequest(new { error = "multipart form with 'file' field required" });
            }

            ZipArchive zip;
            try
            {
                zip = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);
            }
            catch (InvalidDataException)
            {
                return Results.BadRequest(new { error = "invalid zip file" });
            }

            using (zip)
            {
                var mediaEntries = new Dictionary<string, ZipArchiveEntry>();
                var journalEntries = new List<ZipArchiveEntry>();
                var sayEntries = new List<ZipArchiveEntry>();
                JsonDocument? manifest = null;
                foreach (var entry in zip.Entries)
                {
                    if (!IsValidEntryName(entry.FullName))
                    {
                        continue;
                    }

                    if (entry.FullName == "manifest.json")
                    {
                        await using var stream = entry.Open();
                        manifest = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    }
                    else if (entry.FullName.StartsWith("journals/", StringComparison.Ordinal)
                        && entry.FullName.EndsWith(".md", StringComparison.Ordinal))
                    {
                        journalEntries.Add(entry);
                    }
                    else if (entry.FullName.StartsWith("says/", StringComparison.Ordinal)
                        && entry.FullName.EndsWith(".txt", StringComparison.Ordinal))
                    {
                        sayEntries.Add(entry);
                    }
                    else if (entry.FullName.StartsWith("media/", StringComparison.Ordinal))
                    {
                        var storedName = entry.FullName["media/".Length..];
                        if (IsValidStoredName(storedName))
                        {
                            mediaEntries[storedName] = entry;
                        }
                    }
                }

                var mediaInfos = ReadMediaInfos(manifest);

                // 媒体先落盘（storedName 重新生成避免冲突），暂存 MediaAsset 待说说 id 确定后绑定。
                var restoredMedia = new Dictionary<string, MediaAsset>();
                foreach (var (storedName, entry) in mediaEntries)
                {
                    mediaInfos.TryGetValue(storedName, out var info);
                    await using var stream = entry.Open();
                    var newStoredName = await storage.SaveAsync(
                        stream, info.OriginalName ?? storedName, info.ContentType ?? ContentTypeFor(storedName), ct);
                    await using var saved = await storage.OpenAsync(newStoredName);
                    restoredMedia[storedName] = new MediaAsset
                    {
                        Id = Guid.NewGuid(),
                        OwnerUserId = me,
                        StoredName = newStoredName,
                        OriginalName = info.OriginalName ?? storedName,
                        Kind = info.Kind,
                        ContentType = info.ContentType ?? ContentTypeFor(storedName),
                        SizeBytes = saved.Length,
                        ThumbStoredName = null,
                        ContentId = null,
                        CreatedAt = DateTime.UtcNow,
                    };
                }

                foreach (var entry in journalEntries)
                {
                    var text = await ReadAllTextAsync(entry, ct);
                    var (front, body) = ParseFrontMatter(text);
                    var createdAt = ParseDate(front.GetValueOrDefault("createdAt")) ?? DateTime.UtcNow;
                    db.Journals.Add(new Journal
                    {
                        Id = Guid.NewGuid(),
                        AuthorId = me,
                        Title = front.GetValueOrDefault("title") ?? "untitled",
                        MarkdownBody = body,
                        CreatedAt = createdAt,
                        UpdatedAt = ParseDate(front.GetValueOrDefault("updatedAt")) ?? createdAt,
                    });
                }

                foreach (var entry in sayEntries)
                {
                    var raw = await ReadAllTextAsync(entry, ct);
                    var (text, mediaNames) = SplitSayText(raw);
                    var say = new Say
                    {
                        Id = Guid.NewGuid(),
                        AuthorId = me,
                        Text = text,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    };
                    db.Says.Add(say);
                    foreach (var name in mediaNames)
                    {
                        if (restoredMedia.Remove(name, out var asset))
                        {
                            asset.ContentId = say.Id;
                            db.MediaAssets.Add(asset);
                        }
                    }
                }

                await db.SaveChangesAsync(ct);
            }

            return Results.Ok(new { imported = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 批量编辑 @ 名单：多选内容 → 批量追加 @ 对象。仅处理调用者自己的内容（其余跳过）；
        // 已有成员不重复添加；新增者必须是调用者的好友；每条新成员写 Mentioned 站内信。
        app.MapPost("/contents/batch-recipients", [Authorize] async (
            BatchRecipientsRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var contentIds = (request.ContentIds ?? new List<Guid>()).Distinct().ToList();
            var addIds = (request.AddRecipientIds ?? new List<Guid>()).Distinct().ToList();
            if (addIds.Count == 0)
            {
                return Results.Ok(new { updated = 0 });
            }

            var users = await db.Users
                .Where(u => addIds.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync(ct);
            if (users.Count != addIds.Count)
            {
                return Results.BadRequest(new { error = "recipient user not found" });
            }

            var friendships = await db.Friendships
                .Where(f => f.Status == FriendshipStatus.Accepted)
                .Where(f => f.RequesterId == me || f.AddresseeId == me)
                .Select(f => new { f.RequesterId, f.AddresseeId })
                .ToListAsync(ct);
            var friendIds = friendships
                .Select(f => f.RequesterId == me ? f.AddresseeId : f.RequesterId)
                .ToList();
            if (addIds.Any(id => !friendIds.Contains(id)))
            {
                return Results.BadRequest(new { error = "recipient is not an accepted friend" });
            }

            var contents = await db.Contents
                .Where(c => contentIds.Contains(c.Id))
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            var updated = 0;
            foreach (var content in contents)
            {
                // 非作者内容静默跳过，不泄露存在性。
                if (content.AuthorId != me)
                {
                    continue;
                }

                var existing = await db.ContentRecipients
                    .Where(r => r.ContentId == content.Id)
                    .Select(r => r.RecipientUserId)
                    .ToListAsync(ct);
                var added = false;
                foreach (var addId in addIds)
                {
                    if (existing.Contains(addId))
                    {
                        continue;
                    }

                    db.ContentRecipients.Add(new ContentRecipient
                    {
                        Id = Guid.NewGuid(),
                        ContentId = content.Id,
                        RecipientUserId = addId,
                        AddedAt = now,
                    });
                    if (addId != me)
                    {
                        db.InboxMessages.Add(new InboxMessage
                        {
                            Id = Guid.NewGuid(),
                            UserId = addId,
                            Type = InboxMessageType.Mentioned,
                            ContentId = content.Id,
                            CreatedAt = now,
                        });
                    }

                    added = true;
                }

                if (added)
                {
                    updated++;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { updated });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    public sealed record BatchRecipientsRequest(List<Guid>? ContentIds, List<Guid>? AddRecipientIds);

    /// <summary>日志导出 md：frontmatter（title/createdAt/updatedAt/recipients 昵称）+ 正文。</summary>
    private static string BuildJournalMarkdown(Journal journal, List<string> recipientNicknames)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.AppendLine($"title: {journal.Title.ReplaceLineEndings(" ")}");
        builder.AppendLine($"createdAt: {FormatDate(journal.CreatedAt)}");
        builder.AppendLine($"updatedAt: {FormatDate(journal.UpdatedAt)}");
        builder.AppendLine($"recipients: {string.Join(", ", recipientNicknames)}");
        builder.AppendLine("---");
        // 正文原样追加（不加尾随换行），回导时逐字节还原。
        builder.Append(journal.MarkdownBody);
        return builder.ToString();
    }

    /// <summary>说说导出 txt：文本 + 分隔行 + 媒体文件名清单（media/{storedName}）。</summary>
    private static string BuildSayText(Say say, List<MediaAsset> media)
    {
        var builder = new StringBuilder();
        builder.AppendLine(say.Text);
        if (media.Count > 0)
        {
            builder.AppendLine(MediaMarker);
            foreach (var asset in media)
            {
                builder.AppendLine($"media/{asset.StoredName}");
            }
        }

        return builder.ToString();
    }

    /// <summary>解析说说 txt：分隔行之前为文本，之后每行一个媒体路径。</summary>
    private static (string Text, List<string> MediaNames) SplitSayText(string raw)
    {
        var lines = raw.ReplaceLineEndings("\n").Split('\n');
        var markerIndex = Array.FindIndex(lines, l => l.TrimEnd('\r') == MediaMarker);
        if (markerIndex < 0)
        {
            return (raw.TrimEnd('\n'), new List<string>());
        }

        var text = string.Join('\n', lines.Take(markerIndex)).TrimEnd('\n');
        var mediaNames = lines[(markerIndex + 1)..]
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("media/", StringComparison.Ordinal))
            .Select(l => l["media/".Length..])
            .Where(IsValidStoredName)
            .ToList();
        return (text, mediaNames);
    }

    /// <summary>解析 md frontmatter：--- 之间每行 key: value；返回键值对与正文。</summary>
    private static (Dictionary<string, string> Front, string Body) ParseFrontMatter(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var front = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() != "---")
        {
            return (front, text);
        }

        var index = Array.FindIndex(lines, l => l.Trim() == "---") + 1;
        var endIndex = -1;
        for (var i = index; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
            {
                endIndex = i;
                break;
            }

            var separator = lines[i].IndexOf(':');
            if (separator > 0)
            {
                front[lines[i][..separator].Trim()] = lines[i][(separator + 1)..].Trim();
            }
        }

        if (endIndex < 0)
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), text);
        }

        var body = string.Join('\n', lines[(endIndex + 1)..]);
        return (front, body);
    }

    /// <summary>manifest 的媒体映射：storedName → kind/contentType/originalName。</summary>
    private static Dictionary<string, (MediaKind Kind, string ContentType, string OriginalName)> ReadMediaInfos(
        JsonDocument? manifest)
    {
        var result = new Dictionary<string, (MediaKind, string, string)>();
        if (manifest is not null && manifest.RootElement.TryGetProperty("media", out var media))
        {
            foreach (var item in media.EnumerateArray())
            {
                var storedName = item.TryGetProperty("storedName", out var name) ? name.GetString() : null;
                if (storedName is null || !IsValidStoredName(storedName))
                {
                    continue;
                }

                var kind = item.TryGetProperty("kind", out var kindEl)
                    && Enum.TryParse<MediaKind>(kindEl.GetString(), ignoreCase: true, out var parsed)
                        ? parsed
                        : MediaKind.Image;
                var contentType = item.TryGetProperty("contentType", out var ctEl) ? ctEl.GetString() : null;
                var originalName = item.TryGetProperty("originalName", out var origEl) ? origEl.GetString() : null;
                result[storedName] = (kind, contentType ?? ContentTypeFor(storedName), originalName ?? storedName);
            }
        }

        return result;
    }

    /// <summary>从扩展名兜底 Content-Type（manifest 缺失或字段不全时）。</summary>
    private static string ContentTypeFor(string storedName)
    {
        var extension = Path.GetExtension(storedName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".avi" => "video/x-msvideo",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            _ => "application/octet-stream",
        };
    }

    private static DateTime? ParseDate(string? value)
    {
        return value is not null
            && DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    // SQLite 读回的 DateTime 是 Unspecified；库内值一律为 UTC，显式指定后 "O" 才带 Z 后缀。
    private static string FormatDate(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    /// <summary>标题 → 安全文件名：剔除非法字符，压缩空白，限长，空则 untitled。</summary>
    private static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Where(c => !invalid.Contains(c)).ToArray());
        safe = string.Join('-', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (safe.Length > 50)
        {
            safe = safe[..50];
        }

        return safe.Length == 0 ? "untitled" : safe;
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8];

    /// <summary>zip 条目名校验：拒绝路径穿越、盘符与绝对路径。</summary>
    private static bool IsValidEntryName(string name)
    {
        return name.Length > 0
            && !name.Contains("..")
            && !name.Contains(':')
            && !name.StartsWith('/')
            && !name.StartsWith('\\')
            && !Path.IsPathRooted(name);
    }

    private static bool IsValidStoredName(string storedName)
    {
        return ValidStoredName.IsMatch(storedName) && !storedName.Contains("..");
    }

    private static async Task<string> ReadAllTextAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}
