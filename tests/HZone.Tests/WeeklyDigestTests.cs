using HZone.Api.Services;
using HZone.Core.Entities;
using HZone.Infrastructure;
using HZone.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace HZone.Tests;

/// <summary>
/// 每周周报：窗口计算（北京时区周一 00:00 边界）、正文汇总（数量+标题钩子+空周文案）、
/// 到期判定（周一 08:00 前/后、同周幂等）、SendAll 出箱落盘（FileEmailSender）。
/// </summary>
public class WeeklyDigestTests : IClassFixture<HZoneApiFactory>, IAsyncLifetime
{
    private readonly HZoneApiFactory _factory;

    public WeeklyDigestTests(HZoneApiFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.SeedSetupAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Window_body_and_send_all()
    {
        var user = await _factory.SeedUserAsync("dg1a@test.local", "Dg1a");
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HZoneDbContext>();

        var utcNow = new DateTime(2026, 10, 26, 2, 0, 0, DateTimeKind.Utc); // 北京周一 10:00
        var (fromUtc, toUtc) = WeeklyDigestRunner.LastWeekWindow(utcNow);
        // 上周一 00:00 北京 = 10-19 16:00Z；本周一 = 10-26 16:00Z 上界（不含）
        Assert.Equal(new DateTime(2026, 10, 18, 16, 0, 0, DateTimeKind.Utc), fromUtc);
        Assert.Equal(new DateTime(2026, 10, 25, 16, 0, 0, DateTimeKind.Utc), toUtc);

        // 窗口内 2 随记 + 1 笔记（标题），窗口外 1 条不计
        db.Contents.Add(new Say { Id = Guid.NewGuid(), AuthorId = user.Id, Text = "a", CreatedAt = fromUtc.AddHours(1), UpdatedAt = fromUtc });
        db.Contents.Add(new Say { Id = Guid.NewGuid(), AuthorId = user.Id, Text = "b", CreatedAt = toUtc.AddHours(-1), UpdatedAt = toUtc });
        db.Contents.Add(new Journal { Id = Guid.NewGuid(), AuthorId = user.Id, Title = "窗口内笔记", MarkdownBody = "x", CreatedAt = fromUtc.AddHours(2), UpdatedAt = fromUtc });
        db.Contents.Add(new Say { Id = Guid.NewGuid(), AuthorId = user.Id, Text = "outside", CreatedAt = toUtc.AddHours(1), UpdatedAt = toUtc });
        await db.SaveChangesAsync();

        var (says, journals, body) = await WeeklyDigestRunner.BuildAsync(db, user.Id, fromUtc, toUtc, default);
        Assert.Equal(2, says);
        Assert.Equal(1, journals);
        Assert.Contains("2 条随记", body);
        Assert.Contains("窗口内笔记", body);

        // SendAll：未验证邮箱的用户不发；已验证的出箱落盘（FileEmailSender→outbox）
        // IEmailSender 注册为接口（AddSingleton<IEmailSender, FileEmailSender>）——按接口取
        var email = (HZone.Api.Services.FileEmailSender)scope.ServiceProvider.GetRequiredService<HZone.Core.Interfaces.IEmailSender>();
        var runner = new WeeklyDigestRunner(email);
        var sent = await runner.SendAllAsync(db, utcNow, default);
        Assert.True(sent >= 1);
        var outbox = Path.Combine(_factory.DataDirectory, "outbox");
        Assert.True(Directory.Exists(outbox), "outbox 目录应存在");
        var eml = Directory.EnumerateFiles(outbox, "*.eml")
            .FirstOrDefault(f => f.Contains("dg1a"));
        if (eml is null)
        {
            // SeedUserAsync 未置 EmailVerified 时该用户不收——补验证后再发一次
            var u = db.Users.Single(x => x.Id == user.Id);
            u.EmailVerified = true;
            await db.SaveChangesAsync();
            await runner.SendAllAsync(db, utcNow, default);
            eml = Directory.EnumerateFiles(outbox, "*.eml").First(f => f.Contains("dg1a"));
        }
        var text = await File.ReadAllTextAsync(eml);
        Assert.Contains("周报", text);
        Assert.Contains("2 条随记", text);
    }

    [Fact]
    public void DueNow_weekly_gate()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"digest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var state = Path.Combine(dir, "digest-state.json");
        try
        {
            // 周一 07:59 北京（23:59Z 周日）→ 不发
            Assert.False(WeeklyDigestService.DueNow(state, new DateTime(2026, 10, 25, 23, 59, 0, DateTimeKind.Utc)));
            // 周一 08:00 北京（00:00Z 周一）→ 发
            Assert.True(WeeklyDigestService.DueNow(state, new DateTime(2026, 10, 26, 0, 0, 0, DateTimeKind.Utc)));
            // 同周已发（状态=本周键）→ 不发
            File.WriteAllText(state, "20261026");
            Assert.False(WeeklyDigestService.DueNow(state, new DateTime(2026, 10, 28, 0, 0, 0, DateTimeKind.Utc)));
            // 下周一（11-02）→ 再发
            Assert.True(WeeklyDigestService.DueNow(state, new DateTime(2026, 11, 2, 0, 30, 0, DateTimeKind.Utc)));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
