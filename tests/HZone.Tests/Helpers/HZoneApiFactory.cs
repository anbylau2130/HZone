using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HZone.Tests.Helpers;

/// <summary>
/// 进程内起真实 API（HTTP 边界 seam）。每张测试用例一个独立实例。
/// </summary>
public sealed class HZoneApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // 后续 ticket 02 起：数据目录/SQLite 文件/媒体目录重定向到临时路径
    }
}
