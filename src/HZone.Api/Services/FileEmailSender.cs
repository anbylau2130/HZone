using System.Text;
using HZone.Core.Interfaces;

namespace HZone.Api.Services;

/// <summary>
/// 本地/测试环境邮件实现：把 .eml 文本写入 {HZone:DataDirectory}/outbox/，不依赖真实 SMTP。
/// SMTP 真实实现由 ticket 11（管理员配置 SMTP 后按配置选择）提供。
/// </summary>
public sealed class FileEmailSender : IEmailSender
{
    private readonly string _outboxDirectory;

    public FileEmailSender(IConfiguration configuration)
    {
        var dataDirectory = configuration["HZone:DataDirectory"] ?? "data";
        _outboxDirectory = Path.Combine(dataDirectory, "outbox");
    }

    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outboxDirectory);
        var eml = new StringBuilder()
            .AppendLine("From: hzone@localhost")
            .AppendLine($"To: {toEmail}")
            .AppendLine($"Subject: {subject}")
            .AppendLine($"Date: {DateTime.UtcNow:R}")
            .AppendLine("MIME-Version: 1.0")
            .AppendLine("Content-Type: text/plain; charset=utf-8")
            .AppendLine()
            .Append(body)
            .AppendLine();
        var safeEmail = string.Join("_", toEmail.Split(Path.GetInvalidFileNameChars()));
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{safeEmail}.eml";
        await File.WriteAllTextAsync(Path.Combine(_outboxDirectory, fileName), eml.ToString(), ct);
    }
}
