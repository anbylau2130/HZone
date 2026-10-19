using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using HZone.Tests.Helpers;

namespace HZone.Tests;

public class ExportImportTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public ExportImportTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Export_zip_contains_journals_says_media_manifest()
    {
        var author = await _factory.SeedUserAsync("author10a@test.local", "Author10a");
        var friend = await _factory.SeedUserAsync("friend10a@test.local", "Friend10a");
        await _factory.SeedFriendshipAsync(author, friend);

        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        await CreateJournalAsync(client, "Travel Notes", "# trip\nbody text", new[] { friend.Id });
        await CreateSayAsync(client, "hello world", new[] { friend.Id });
        var media = await UploadPngAsync(client, 24, 24);
        await CreateSayAsync(client, "with photo", Array.Empty<Guid>(), new[] { media.Id });

        var export = await client.GetAsync("/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/zip", export.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("hzone-export-", export.Content.Headers.ContentDisposition?.FileName);

        using var zip = new ZipArchive(await export.Content.ReadAsStreamAsync());
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains(names, n => n.StartsWith("journals/") && n.EndsWith(".md"));
        Assert.Equal(2, names.Count(n => n.StartsWith("says/") && n.EndsWith(".txt")));
        Assert.Contains("manifest.json", names);
        Assert.Single(names, n => n.StartsWith("media/"));

        // 日志 md：frontmatter 含 title 与 @ 过的好友昵称。
        var journalEntry = zip.Entries.First(n => n.FullName.StartsWith("journals/"));
        var markdown = await ReadEntryAsync(zip.GetEntry(journalEntry.FullName)!);
        Assert.Contains("title: Travel Notes", markdown);
        Assert.Contains("recipients: Friend10a", markdown);
        Assert.Contains("# trip", markdown);

        // 说说 txt：文本在前，媒体清单在后。
        var allSayText = await ReadAllSayTextsAsync(zip);
        var withPhoto = allSayText.Single(t => t.Contains("--hzone-media--"));
        Assert.Contains("with photo", withPhoto);
        Assert.Contains("media/", withPhoto);
        Assert.Contains(allSayText, t => t.Contains("hello world"));
    }

    [Fact]
    public async Task Export_excludes_other_users_content()
    {
        var author = await _factory.SeedUserAsync("author10b@test.local", "Author10b");
        var friend = await _factory.SeedUserAsync("friend10b@test.local", "Friend10b");
        await _factory.SeedFriendshipAsync(author, friend);

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        await CreateJournalAsync(friendClient, "Friends Private Journal", "secret", Array.Empty<Guid>());

        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        await CreateJournalAsync(client, "My Journal", "mine", Array.Empty<Guid>());

        using var zip = await ExportZipAsync(client);
        var markdowns = await Task.WhenAll(zip.Entries
            .Where(e => e.FullName.StartsWith("journals/"))
            .Select(ReadEntryAsync));
        var allText = string.Join('\n', markdowns);

        Assert.Contains("My Journal", allText);
        Assert.DoesNotContain("Friends Private Journal", allText);
    }

    [Fact]
    public async Task Import_into_clean_instance_restores_content_and_media_privately()
    {
        // 源实例：author 有日志 + 带媒体说说 + 被 @ 的好友。
        var author = await _factory.SeedUserAsync("author10c@test.local", "Author10c");
        var friend = await _factory.SeedUserAsync("friend10c@test.local", "Friend10c");
        await _factory.SeedFriendshipAsync(author, friend);

        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var journalCreatedAt = DateTime.UtcNow.AddHours(-1);
        var journalId = await CreateJournalRawAsync(client, "Backup Journal", "restored body", new[] { friend.Id });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var journal = await db.Journals.SingleAsync(j => j.Id == journalId);
            journal.CreatedAt = journalCreatedAt;
            await db.SaveChangesAsync();
        }

        var media = await UploadPngAsync(client, 32, 32);
        await CreateSayAsync(client, "say to restore", Array.Empty<Guid>(), new[] { media.Id });

        var zipBytes = await ExportBytesAsync(client);

        // 干净实例：仅初始化 + 导入用户。
        using var target = new HZoneApiFactory();
        var importer = await target.SeedSetupAsync();
        Assert.NotNull(importer);
        var targetUser = await target.SeedUserAsync("importer10c@test.local", "Importer10c");
        var targetFriend = await target.SeedUserAsync("tfriend10c@test.local", "TFriend10c");
        await target.SeedFriendshipAsync(targetUser, targetFriend);
        var targetClient = target.CreateAuthorizedClient(targetUser.Id, targetUser.Nickname);

        using (var form = new MultipartFormDataContent())
        {
            var content = new ByteArrayContent(zipBytes);
            content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
            form.Add(content, "file", "export.zip");
            var import = await targetClient.PostAsync("/import", form);
            Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        }

        using (var scope = target.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var journals = await db.Journals.Where(j => j.AuthorId == targetUser.Id).ToListAsync();
            var says = await db.Says.Where(s => s.AuthorId == targetUser.Id).ToListAsync();
            var journal = journals.Single(j => j.Title == "Backup Journal");
            Assert.Equal("restored body", journal.MarkdownBody);
            // createdAt 保留（秒级精度比较，SQLite 存储截断毫秒）。
            // createdAt 保留（秒级精度比较，SQLite 存储截断毫秒）；库内值一律为 UTC，不再做时区转换。
            Assert.Equal(journalCreatedAt.Ticks / TimeSpan.TicksPerSecond, journal.CreatedAt.Ticks / TimeSpan.TicksPerSecond);

            // 说说恢复 + 媒体绑定，storedName 重新生成。
            var say = says.Single(s => s.Text == "say to restore");
            var assets = await db.MediaAssets.Where(m => m.ContentId == say.Id).ToListAsync();
            var asset = Assert.Single(assets);
            Assert.Equal(targetUser.Id, asset.OwnerUserId);
            Assert.NotEqual(media.Url.Substring("/media/".Length), asset.StoredName);
            Assert.True(File.Exists(Path.Combine(target.DataDirectory, "media", asset.StoredName)));

            // 默认私密：无 ContentRecipient 行。
            Assert.False(await db.ContentRecipients.AnyAsync(r => r.ContentId == say.Id || r.ContentId == journal.Id));
        }

        // 导入内容对好友不可见（默认私密）；媒体登录后可下载。
        var targetFriendClient = target.CreateAuthorizedClient(targetFriend.Id, targetFriend.Nickname);
        var importedJournal = await targetClient.GetAsync("/me/contents?kind=journal");
        Assert.Equal(HttpStatusCode.OK, importedJournal.StatusCode);
        var listBody = await importedJournal.Content.ReadFromJsonAsync<JsonElement>();
        var journalJsonId = listBody.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound,
            (await targetFriendClient.GetAsync($"/contents/{journalJsonId}")).StatusCode);
    }

    [Fact]
    public async Task Batch_add_recipients_makes_friend_visible_and_idempotent()
    {
        var author = await _factory.SeedUserAsync("author10d@test.local", "Author10d");
        var friend1 = await _factory.SeedUserAsync("friend10d1@test.local", "Friend10d1");
        var friend2 = await _factory.SeedUserAsync("friend10d2@test.local", "Friend10d2");
        var stranger = await _factory.SeedUserAsync("stranger10d@test.local", "Stranger10d");
        await _factory.SeedFriendshipAsync(author, friend1);
        await _factory.SeedFriendshipAsync(author, friend2);

        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var contentId = await CreateJournalRawAsync(client, "Batch Target", "body", Array.Empty<Guid>());

        // 好友 + 陌生人都加入：非好友 → 400。
        var batch = await client.PostAsJsonAsync("/contents/batch-recipients", new
        {
            contentIds = new[] { contentId },
            addRecipientIds = new[] { friend1.Id, stranger.Id },
        });
        Assert.Equal(HttpStatusCode.BadRequest, batch.StatusCode);

        // 仅好友：200 updated=1；好友可见；写 Mentioned 站内信。
        batch = await client.PostAsJsonAsync("/contents/batch-recipients", new
        {
            contentIds = new[] { contentId },
            addRecipientIds = new[] { friend1.Id },
        });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(1, (await batch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated").GetInt32());

        var friend1Client = _factory.CreateAuthorizedClient(friend1.Id, friend1.Nickname);
        Assert.Equal(HttpStatusCode.OK,
            (await friend1Client.GetAsync($"/contents/{contentId}")).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();
            var message = await db.InboxMessages.SingleAsync(
                m => m.UserId == friend1.Id && m.ContentId == contentId);
            Assert.Equal(InboxMessageType.Mentioned, message.Type);

            // 重复添加：不产生重复行，updated=0。
            var repeat = await client.PostAsJsonAsync("/contents/batch-recipients", new
            {
                contentIds = new[] { contentId },
                addRecipientIds = new[] { friend1.Id },
            });
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            Assert.Equal(0, (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated").GetInt32());
            Assert.Equal(1, await db.ContentRecipients.CountAsync(r => r.ContentId == contentId));
        }
    }

    [Fact]
    public async Task Batch_endpoint_skips_non_author_content_and_reports_count()
    {
        var author = await _factory.SeedUserAsync("author10e@test.local", "Author10e");
        var friend = await _factory.SeedUserAsync("friend10e@test.local", "Friend10e");
        var other = await _factory.SeedUserAsync("other10e@test.local", "Other10e");
        await _factory.SeedFriendshipAsync(author, friend);
        await _factory.SeedFriendshipAsync(other, friend);

        var client = _factory.CreateAuthorizedClient(author.Id, author.Nickname);
        var mine = await CreateJournalRawAsync(client, "Mine", "body", Array.Empty<Guid>());
        var otherClient = _factory.CreateAuthorizedClient(other.Id, other.Nickname);
        var theirs = await CreateJournalRawAsync(otherClient, "Theirs", "body", Array.Empty<Guid>());

        // 批量含自己的与非自己的内容：只处理自己的，updated=1。
        var batch = await client.PostAsJsonAsync("/contents/batch-recipients", new
        {
            contentIds = new[] { mine, theirs },
            addRecipientIds = new[] { friend.Id },
        });
        Assert.Equal(HttpStatusCode.OK, batch.StatusCode);
        Assert.Equal(1, (await batch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated").GetInt32());

        var friendClient = _factory.CreateAuthorizedClient(friend.Id, friend.Nickname);
        Assert.Equal(HttpStatusCode.OK, (await friendClient.GetAsync($"/contents/{mine}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await friendClient.GetAsync($"/contents/{theirs}")).StatusCode);
    }

    [Fact]
    public async Task Export_import_and_batch_require_initialized_instance()
    {
        using var factory = new HZoneApiFactory();
        var user = await factory.SeedUserAsync("uninit10@test.local", "Uninit10");
        var client = factory.CreateAuthorizedClient(user.Id, user.Nickname);

        Assert.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/export")).StatusCode);

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/zip");
        form.Add(content, "file", "x.zip");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/import", form)).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/contents/batch-recipients", new
            {
                contentIds = Array.Empty<Guid>(),
                addRecipientIds = Array.Empty<Guid>(),
            })).StatusCode);
    }

    [Fact]
    public async Task Export_import_and_batch_require_authentication()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/export")).StatusCode);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new byte[] { 1 }), "file", "x.zip");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/import", form)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync("/contents/batch-recipients", new
            {
                contentIds = Array.Empty<Guid>(),
                addRecipientIds = Array.Empty<Guid>(),
            })).StatusCode);
    }

    private static async Task<List<string>> ReadAllSayTextsAsync(ZipArchive zip)
    {
        var texts = new List<string>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("says/")))
        {
            texts.Add(await ReadEntryAsync(entry));
        }

        return texts;
    }

    private static async Task<Guid> CreateJournalAsync(
        HttpClient client, string title, string markdown, IEnumerable<Guid> recipientIds)
    {
        var response = await client.PostAsJsonAsync("/contents/journals", new
        {
            title,
            markdown,
            recipientIds,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static Task<Guid> CreateJournalRawAsync(
        HttpClient client, string title, string markdown, IEnumerable<Guid> recipientIds)
        => CreateJournalAsync(client, title, markdown, recipientIds);

    private static async Task CreateSayAsync(
        HttpClient client, string text, IEnumerable<Guid> recipientIds, IEnumerable<Guid>? mediaIds = null)
    {
        var response = await client.PostAsJsonAsync("/contents/says", new
        {
            text,
            recipientIds,
            mediaIds,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<ZipArchive> ExportZipAsync(HttpClient client)
    {
        var response = await client.GetAsync("/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new ZipArchive(await response.Content.ReadAsStreamAsync());
    }

    private static async Task<byte[]> ExportBytesAsync(HttpClient client)
    {
        var response = await client.GetAsync("/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static async Task<string> ReadEntryAsync(ZipArchiveEntry entry)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task<MediaUpload> UploadPngAsync(HttpClient client, int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgb24((byte)(x * 8), (byte)(y * 8), 128);
                }
            }
        });
        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(content, "file", "photo.png");
        var response = await client.PostAsync("/media", form);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new MediaUpload(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("url").GetString()!,
            body.GetProperty("thumbUrl").GetString());
    }

    private sealed record MediaUpload(Guid Id, string Url, string? ThumbUrl);
}
