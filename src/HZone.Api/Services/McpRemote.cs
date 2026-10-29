using System.Text;
using System.Text.Json;

namespace HZone.Api.Services;

/// <summary>
/// 外部 MCP server 客户端（Streamable HTTP 简化实现）：每次请求先 initialize
/// 再目标方法（无状态，多数 HTTP MCP server 兼容）。供 AI 对话注入外部工具。
/// </summary>
public class McpRemote(IHttpClientFactory httpFactory)
{
    public class RemoteTool
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public JsonElement InputSchema { get; set; }
    }

    private record McpServerInfo(string Url, string? AuthHeader);

    // 工具列表缓存（key = server url；10 分钟 TTL，失败即失效重拉）
    private static readonly Dictionary<string, (DateTime At, List<RemoteTool> Tools)> Cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private static readonly object CacheLock = new();

    public async Task<List<RemoteTool>> ListToolsAsync(string url, string? authHeader, CancellationToken ct)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(url, out var hit) && DateTime.UtcNow - hit.At < Ttl)
            {
                return hit.Tools;
            }
        }
        var init = await RpcAsync<McpEmpty>(url, authHeader, "initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "HZone", version = "1.0" },
        }, ct);
        var list = await RpcAsync<McpToolsResult>(url, authHeader, "tools/list", new { }, ct);
        var tools = list.Tools ?? [];
        lock (CacheLock)
        {
            Cache[url] = (DateTime.UtcNow, tools);
        }
        return tools;
    }

    public async Task<string> CallToolAsync(
        string url, string? authHeader, string name, JsonElement args, CancellationToken ct)
    {
        var result = await RpcAsync<McpCallResult>(url, authHeader, "tools/call", new
        {
            name,
            arguments = args,
        }, ct);
        var text = result.Content?.FirstOrDefault(c => c.TryGetValue("type", out var t) && t.GetString() == "text");
        if (text != null && text.TryGetValue("text", out var v))
        {
            return v.GetString() ?? "";
        }
        return JsonSerializer.Serialize(result);
    }

    /// <summary>工具列表缓存强制失效（server 配置变更时调用）。</summary>
    public static void InvalidateCache(string? url = null)
    {
        lock (CacheLock)
        {
            if (url is null) Cache.Clear();
            else Cache.Remove(url);
        }
    }

    private async Task<T> RpcAsync<T>(string url, string? authHeader, string method, object @params, CancellationToken ct)
    {
        var client = httpFactory.CreateClient("mcp");
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { jsonrpc = "2.0", id = Random.Shared.Next(1, int.MaxValue), method, @params }),
                Encoding.UTF8, "application/json"),
        };
        // AuthHeader 形如 "Authorization: Bearer xxx"
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            var idx = authHeader.IndexOf(':');
            if (idx > 0)
            {
                req.Headers.TryAddWithoutValidation(
                    authHeader[..idx].Trim(), authHeader[(idx + 1)..].Trim());
            }
        }
        req.Headers.Accept.Add(new("application/json"));
        req.Headers.Accept.Add(new("text/event-stream"));
        using var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadAsStringAsync(ct);
        // SSE 响应（event-stream）：取首个 data: 行的 JSON
        if (res.Content.Headers.ContentType?.MediaType == "text/event-stream"
            || body.StartsWith("event:", StringComparison.Ordinal)
            || body.StartsWith("data:", StringComparison.Ordinal))
        {
            var sb = new StringBuilder();
            foreach (var line in body.Split('\n'))
            {
                if (line.StartsWith("data:", StringComparison.Ordinal)) sb.AppendLine(line[5..].Trim());
            }
            body = sb.ToString();
        }
        var envelope = JsonDocument.Parse(body).RootElement;
        if (envelope.TryGetProperty("error", out var err))
        {
            throw new InvalidOperationException($"MCP error: {err}");
        }
        return envelope.GetProperty("result").Deserialize<T>() ?? throw new InvalidOperationException("empty result");
    }

    private record McpEmpty;

    private class McpToolsResult
    {
        public List<RemoteTool> Tools { get; set; } = [];
    }

    private class McpCallResult
    {
        public List<Dictionary<string, JsonElement>>? Content { get; set; }
    }
}
