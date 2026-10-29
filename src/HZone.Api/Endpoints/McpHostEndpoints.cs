using System.Security.Claims;
using System.Text.Json;
using HZone.Api.Services;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// 对外 MCP Server（JSON-RPC over HTTP，Streamable HTTP 单 POST 模式）：
/// 外部 AI 客户端（Claude Desktop / ZCode 等）用 PAT 认证读写 HZone 数据。
/// 协议方法：initialize / ping / tools/list / tools/call。
/// </summary>
public static class McpHostEndpoints
{
    public static void MapMcpHostEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/mcp", [Authorize] async (
            HttpRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            string? bodyText;
            using (var reader = new StreamReader(request.Body))
            {
                bodyText = await reader.ReadToEndAsync(ct);
            }
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(bodyText ?? "");
            }
            catch
            {
                return Results.Json(new { jsonrpc = "2.0", error = new { code = -32700, message = "parse error" } });
            }
            using var _ = doc;
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var methodEl))
            {
                return Results.Json(new { jsonrpc = "2.0", error = new { code = -32600, message = "invalid request" } });
            }
            var method = methodEl.GetString() ?? "";
            // id 需 Clone 脱离文档：Results.Json 延迟序列化时 doc 已被 using 释放
            var id = root.TryGetProperty("id", out var idEl) ? (object?)idEl.Clone() : null;

            object? result = method switch
            {
                "initialize" => new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { tools = new { listChanged = false } },
                    serverInfo = new { name = "HZone", version = "1.0.0" },
                },
                "ping" => new { },
                "tools/list" => new
                {
                    tools = AiTools.Definitions.Select(d => new
                    {
                        name = $"hz_{d.Name}",
                        description = d.Description,
                        inputSchema = d.Parameters,
                    }).ToList(),
                },
                "tools/call" => await CallToolAsync(db, me, root, ct),
                _ => null,
            };
            if (result is null)
            {
                return Results.Json(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32601, message = $"method not found: {method}" },
                });
            }
            return Results.Json(new { jsonrpc = "2.0", id, result });
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static async Task<object> CallToolAsync(
        HZoneDbContext db, Guid userId, JsonElement root, CancellationToken ct)
    {
        var name = root.GetProperty("params").GetProperty("name").GetString() ?? "";
        // 前缀剥离：hz_search_content → search_content
        if (name.StartsWith("hz_", StringComparison.Ordinal))
        {
            name = name[3..];
        }
        var args = root.GetProperty("params").TryGetProperty("arguments", out var a)
            && a.ValueKind == JsonValueKind.Object
            ? a.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();
        var output = await AiTools.ExecuteAsync(db, userId, name, args, ct);
        return new
        {
            content = new[] { new { type = "text", text = output } },
        };
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}
