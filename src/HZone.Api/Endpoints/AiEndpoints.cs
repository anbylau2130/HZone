using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HZone.Api.Services;
using HZone.Core.Entities;
using HZone.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HZone.Api.Endpoints;

/// <summary>
/// AI 对话：OpenAI 兼容协议（每用户 BaseUrl/Key/Model，key 只写不回显）。
/// 多会话持久化；技能 = 预设指令模板；工具 = 内置（AiTools 读写 HZone 数据）
/// + 外部 MCP server（McpRemote，工具名加前缀）；SSE 流式输出（delta/tool/done）。
/// </summary>
public static class AiEndpoints
{
    private const int MaxHistoryMessages = 30;
    private const int MaxToolRounds = 5;

    public static void MapAiEndpoints(this IEndpointRouteBuilder app)
    {
        // ---------- Provider 配置 ----------
        app.MapGet("/me/ai/provider", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var p = await db.AiProviderSettings.AsNoTracking()
                .Where(x => x.UserId == me)
                .Select(x => new { x.BaseUrl, x.Model, HasKey = x.ApiKey != null && x.ApiKey != "" })
                .FirstOrDefaultAsync(ct);
            return Results.Ok(p ?? new { BaseUrl = "", Model = "", HasKey = false });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPut("/me/ai/provider", [Authorize] async (
            SaveProviderRequest request,
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var baseUrl = request.BaseUrl?.Trim().TrimEnd('/') ?? "";
            var model = request.Model?.Trim() ?? "";
            if (baseUrl.Length > 500 || model.Length > 100
                || (!string.IsNullOrEmpty(baseUrl)
                    && !baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                return Results.BadRequest(new { error = "invalid baseUrl or model" });
            }
            var p = await db.AiProviderSettings.FirstOrDefaultAsync(x => x.UserId == me, ct);
            if (p is null)
            {
                p = new AiProviderSetting { UserId = me };
                db.AiProviderSettings.Add(p);
            }
            p.BaseUrl = baseUrl;
            p.Model = model;
            if (!string.IsNullOrEmpty(request.ApiKey)) p.ApiKey = request.ApiKey.Trim();
            p.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 连接测试：发一次 1 token 请求验证 baseUrl/key/model
        app.MapPost("/me/ai/provider/test", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var p = await db.AiProviderSettings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == me, ct);
            if (p is null || string.IsNullOrEmpty(p.BaseUrl) || string.IsNullOrEmpty(p.ApiKey))
            {
                return Results.BadRequest(new { error = "provider not configured" });
            }
            try
            {
                var client = httpFactory.CreateClient("ai");
                client.DefaultRequestHeaders.Authorization = new("Bearer", p.ApiKey);
                using var res = await client.PostAsync(
                    $"{p.BaseUrl}/chat/completions",
                    new StringContent(JsonSerializer.Serialize(new
                    {
                        model = p.Model,
                        messages = new[] { new { role = "user", content = "ping" } },
                        max_tokens = 5,
                    }), Encoding.UTF8, "application/json"), ct);
                var body = await res.Content.ReadAsStringAsync(ct);
                return Results.Ok(new { ok = res.IsSuccessStatusCode, status = (int)res.StatusCode, body = body[..Math.Min(300, body.Length)] });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 技能 ----------
        app.MapGet("/me/ai/skills", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var items = await db.AiSkills.AsNoTracking()
                .Where(s => s.UserId == me)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.Name, s.Icon, s.Prompt, s.CreatedAt })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/me/ai/skills", [Authorize] async (
            SaveSkillRequest request,
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var (name, icon, prompt, err) = ValidateSkill(request);
            if (err is not null) return Results.BadRequest(new { error = err });
            var skill = new AiSkill
            {
                Id = Guid.NewGuid(), UserId = me, Name = name!, Icon = icon!, Prompt = prompt!,
                CreatedAt = DateTime.UtcNow,
            };
            db.AiSkills.Add(skill);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/ai/skills/{skill.Id}", new { skill.Id, skill.Name, skill.Icon, skill.Prompt, skill.CreatedAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPut("/me/ai/skills/{id:guid}", [Authorize] async (
            Guid id, SaveSkillRequest request,
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var skill = await db.AiSkills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct);
            if (skill is null) return Results.NotFound(new { error = "skill not found" });
            var (name, icon, prompt, err) = ValidateSkill(request);
            if (err is not null) return Results.BadRequest(new { error = err });
            skill.Name = name!;
            skill.Icon = icon!;
            skill.Prompt = prompt!;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { skill.Id });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapDelete("/me/ai/skills/{id:guid}", [Authorize] async (
            Guid id, ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var skill = await db.AiSkills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct);
            if (skill is null) return Results.NotFound(new { error = "skill not found" });
            db.AiSkills.Remove(skill);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 外部 MCP server ----------
        app.MapGet("/me/ai/mcp-servers", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var items = await db.AiMcpServers.AsNoTracking()
                .Where(s => s.UserId == me)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new { s.Id, s.Name, s.Url, HasAuth = s.AuthHeader != null && s.AuthHeader != "", s.Enabled, s.CreatedAt })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/me/ai/mcp-servers", [Authorize] async (
            SaveMcpServerRequest request,
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var (name, url, err) = ValidateMcp(request);
            if (err is not null) return Results.BadRequest(new { error = err });
            var server = new AiMcpServer
            {
                Id = Guid.NewGuid(), UserId = me, Name = name!, Url = url!,
                AuthHeader = string.IsNullOrWhiteSpace(request.AuthHeader) ? null : request.AuthHeader.Trim(),
                Enabled = true, CreatedAt = DateTime.UtcNow,
            };
            db.AiMcpServers.Add(server);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/ai/mcp-servers/{server.Id}",
                new { server.Id, server.Name, server.Url, HasAuth = server.AuthHeader != null, server.Enabled, server.CreatedAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPut("/me/ai/mcp-servers/{id:guid}", [Authorize] async (
            Guid id, SaveMcpServerRequest request,
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var server = await db.AiMcpServers.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct);
            if (server is null) return Results.NotFound(new { error = "mcp server not found" });
            var (name, url, err) = ValidateMcp(request);
            if (err is not null) return Results.BadRequest(new { error = err });
            server.Name = name!;
            server.Url = url!;
            if (!string.IsNullOrWhiteSpace(request.AuthHeader)) server.AuthHeader = request.AuthHeader.Trim();
            if (request.Enabled is { } en) server.Enabled = en;
            McpRemote.InvalidateCache();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { server.Id });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapDelete("/me/ai/mcp-servers/{id:guid}", [Authorize] async (
            Guid id, ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var server = await db.AiMcpServers.FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct);
            if (server is null) return Results.NotFound(new { error = "mcp server not found" });
            McpRemote.InvalidateCache(server.Url);
            db.AiMcpServers.Remove(server);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // 工具预览：拉取该 server 的工具列表（连通性验证）
        app.MapGet("/me/ai/mcp-servers/{id:guid}/tools", [Authorize] async (
            Guid id, ClaimsPrincipal principal, HZoneDbContext db, McpRemote mcp, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var server = await db.AiMcpServers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == me, ct);
            if (server is null) return Results.NotFound(new { error = "mcp server not found" });
            try
            {
                var tools = await mcp.ListToolsAsync(server.Url, server.AuthHeader, ct);
                return Results.Ok(new
                {
                    ok = true,
                    tools = tools.Select(t => new { t.Name, t.Description }).ToList(),
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 会话 ----------
        app.MapGet("/me/ai/conversations", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var items = await db.AiConversations.AsNoTracking()
                .Where(c => c.UserId == me)
                .OrderByDescending(c => c.UpdatedAt)
                .Select(c => new { c.Id, c.Title, c.UpdatedAt })
                .ToListAsync(ct);
            return Results.Ok(new { items });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapPost("/me/ai/conversations", [Authorize] async (
            ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var conversation = new AiConversation
            {
                Id = Guid.NewGuid(), UserId = me, Title = "新对话",
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            };
            db.AiConversations.Add(conversation);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/me/ai/conversations/{conversation.Id}",
                new { conversation.Id, conversation.Title, conversation.UpdatedAt });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapDelete("/me/ai/conversations/{id:guid}", [Authorize] async (
            Guid id, ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var conversation = await db.AiConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == me, ct);
            if (conversation is null) return Results.NotFound(new { error = "conversation not found" });
            db.AiConversations.Remove(conversation);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { ok = true });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        app.MapGet("/me/ai/conversations/{id:guid}/messages", [Authorize] async (
            Guid id, ClaimsPrincipal principal, HZoneDbContext db, CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var conversation = await db.AiConversations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == me, ct);
            if (conversation is null) return Results.NotFound(new { error = "conversation not found" });
            var items = await db.AiMessages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id, m.Role, m.Content, m.ToolCallsJson, m.ToolCallId, m.ToolName, m.CreatedAt,
                })
                .ToListAsync(ct);
            return Results.Ok(new { conversation.Id, conversation.Title, items });
        }).AddEndpointFilter(SetupState.RequireInitialized);

        // ---------- 对话主端点（SSE 流式 + 工具循环） ----------
        app.MapPost("/me/ai/conversations/{id:guid}/chat", [Authorize] async (
            Guid id,
            ChatRequest request,
            ClaimsPrincipal principal,
            HZoneDbContext db,
            IHttpClientFactory httpFactory,
            McpRemote mcp,
            CancellationToken ct) =>
        {
            var me = CurrentUserId(principal);
            var conversation = await db.AiConversations
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == me, ct);
            if (conversation is null)
            {
                return Results.NotFound(new { error = "conversation not found" });
            }
            var provider = await db.AiProviderSettings.AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == me, ct);
            if (provider is null || string.IsNullOrEmpty(provider.BaseUrl)
                || string.IsNullOrEmpty(provider.ApiKey) || string.IsNullOrEmpty(provider.Model))
            {
                return Results.BadRequest(new { error = "ai provider not configured" });
            }
            var content = request.Content?.Trim() ?? "";
            if (content.Length is < 1 or > 20_000)
            {
                return Results.BadRequest(new { error = "content required (max 20000 chars)" });
            }

            // 技能（可选）：本次消息附加 system 指令
            string? skillPrompt = null;
            if (request.SkillId is { } sid)
            {
                skillPrompt = await db.AiSkills.AsNoTracking()
                    .Where(s => s.Id == sid && s.UserId == me)
                    .Select(s => s.Prompt)
                    .FirstOrDefaultAsync(ct);
            }

            // user 消息先落库 + 首次对话取标题
            var now = DateTime.UtcNow;
            db.AiMessages.Add(new AiMessage
            {
                Id = Guid.NewGuid(), ConversationId = id, Role = "user", Content = content, CreatedAt = now,
            });
            if (conversation.Title == "新对话")
            {
                conversation.Title = content.Length > 20 ? content[..20] + "…" : content;
            }
            conversation.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            // 外部 MCP 工具：enabled server 拉列表（失败跳过）
            var mcpServers = await db.AiMcpServers.AsNoTracking()
                .Where(s => s.UserId == me && s.Enabled)
                .ToListAsync(ct);
            var remoteTools = new List<(Guid ServerId, string ServerName, string Url, string? Auth, McpRemote.RemoteTool Tool)>();
            foreach (var s in mcpServers)
            {
                try
                {
                    var remoteToolList = await mcp.ListToolsAsync(s.Url, s.AuthHeader, ct);
                    remoteTools.AddRange(remoteToolList.Select(t => (s.Id, s.Name, s.Url, s.AuthHeader, t)));
                }
                catch
                {
                    // 单个 server 失联不阻断对话
                }
            }

            // OpenAI 消息组装：system（技能 + 工具说明 + 日期）+ 历史 + user
            var history = await db.AiMessages.AsNoTracking()
                .Where(m => m.ConversationId == id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(MaxHistoryMessages)
                .ToListAsync(ct);
            history.Reverse();
            var messages = new List<Dictionary<string, object?>>();
            var systemParts = new List<string>
            {
                $"你是 HZone 私密记录社区的 AI 助手。今天是 {DateTime.UtcNow:yyyy-MM-dd}。" +
                "你可以调用工具读写用户的数据（搜索内容/读笔记/查记账/记一笔/建笔记）。金额单位是分（cents），1 元 = 100 分。",
            };
            if (skillPrompt is not null) systemParts.Add($"用户指定的技能指令：\n{skillPrompt}");
            if (remoteTools.Count > 0)
            {
                systemParts.Add("另有外部 MCP 工具可用，工具名以 mcp_ 前缀标识。");
            }
            messages.Add(new() { ["role"] = "system", ["content"] = string.Join("\n\n", systemParts) });
            foreach (var m in history)
            {
                if (m.Role == "user")
                {
                    messages.Add(new() { ["role"] = "user", ["content"] = m.Content });
                }
                else if (m.Role == "assistant")
                {
                    var msg = new Dictionary<string, object?> { ["role"] = "assistant", ["content"] = m.Content };
                    if (m.ToolCallsJson is { Length: > 0 } tc)
                    {
                        msg["tool_calls"] = JsonDocument.Parse(tc).RootElement;
                    }
                    messages.Add(msg);
                }
                else if (m.Role == "tool" && m.ToolCallId is not null)
                {
                    var toolContent = m.Content.Length > 4000 ? m.Content[..4000] : m.Content;
                    messages.Add(new()
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = m.ToolCallId,
                        ["content"] = toolContent,
                    });
                }
            }

            // tools 定义：内置 + 外部（前缀 mcp_{serverId8}_）
            var tools = AiTools.OpenAiTools();
            foreach (var (serverId, _, _, _, tool) in remoteTools)
            {
                tools.Add(new
                {
                    type = "function",
                    function = new
                    {
                        name = $"mcp_{serverId.ToString("N")[..8]}_{tool.Name}",
                        description = tool.Description,
                        parameters = tool.InputSchema,
                    },
                });
            }

            // 捕获供流闭包使用
            var providerCapture = provider;
            var remoteToolsCapture = remoteTools;
            var conversationId = id;

            return Results.Stream(async stream =>
            {
                var client = httpFactory.CreateClient("ai");
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Authorization = new("Bearer", providerCapture.ApiKey);
                var encoder = Encoding.UTF8;

                async Task Sse(string ev, object data)
                {
                    var bytes = encoder.GetBytes($"event: {ev}\ndata: {JsonSerializer.Serialize(data)}\n\n");
                    await stream.WriteAsync(bytes);
                    await stream.FlushAsync();
                }

                async Task<string?> SseError(string msg)
                {
                    await Sse("error", new { error = msg });
                    return null;
                }

                try
                {
                    await Sse("start", new { conversationId });

                    for (var round = 0; round <= MaxToolRounds; round++)
                    {
                        // 调 OpenAI 兼容 /chat/completions（流式）
                        var requestBody = JsonSerializer.Serialize(new
                        {
                            model = providerCapture.Model,
                            messages = messages.Select(Compact).ToList(),
                            tools = tools.Count > 0 ? tools : null,
                            stream = true,
                        });
                        using var req = new HttpRequestMessage(HttpMethod.Post, $"{providerCapture.BaseUrl}/chat/completions")
                        {
                            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
                        };
                        using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                        if (!res.IsSuccessStatusCode)
                        {
                            var errBody = await res.Content.ReadAsStringAsync();
                            await SseError($"模型服务返回 {(int)res.StatusCode}: {errBody[..Math.Min(300, errBody.Length)]}");
                            return;
                        }

                        // SSE 解析：累计 content 与 tool_calls 分片
                        var contentBuilder = new StringBuilder();
                        var toolCallAcc = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)>();
                        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
                        while (!reader.EndOfStream)
                        {
                            var line = await reader.ReadLineAsync();
                            if (line is null || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
                            var payload = line[5..].Trim();
                            if (payload == "[DONE]") break;
                            using var chunk = JsonDocument.Parse(payload);
                            var delta = chunk.RootElement.GetProperty("choices")[0].GetProperty("delta");
                            if (delta.ValueKind == JsonValueKind.Undefined) continue;
                            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                                && !string.IsNullOrEmpty(c.GetString()))
                            {
                                var piece = c.GetString()!;
                                contentBuilder.Append(piece);
                                await Sse("delta", new { text = piece });
                            }
                            if (delta.TryGetProperty("tool_calls", out var tcs))
                            {
                                foreach (var tc in tcs.EnumerateArray())
                                {
                                    var idx = tc.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0;
                                    if (!toolCallAcc.TryGetValue(idx, out var acc))
                                    {
                                        acc = (null, null, new StringBuilder());
                                        toolCallAcc[idx] = acc;
                                    }
                                    if (tc.TryGetProperty("id", out var tid) && tid.ValueKind == JsonValueKind.String)
                                        toolCallAcc[idx] = (tid.GetString(), acc.Name, acc.Args);
                                    var fn = tc.TryGetProperty("function", out var fnEl) ? fnEl : default;
                                    if (fn.ValueKind != JsonValueKind.Undefined)
                                    {
                                        if (fn.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String)
                                        {
                                            var merged = toolCallAcc[idx];
                                            toolCallAcc[idx] = (merged.Id, nm.GetString(), merged.Args);
                                        }
                                        if (fn.TryGetProperty("arguments", out var ag) && ag.ValueKind == JsonValueKind.String)
                                            toolCallAcc[idx].Args.Append(ag.GetString());
                                    }
                                }
                            }
                        }

                        // 无工具调用：收尾
                        if (toolCallAcc.Count == 0)
                        {
                            var finalText = contentBuilder.ToString();
                            if (finalText.Length > 0)
                            {
                                db.AiMessages.Add(new AiMessage
                                {
                                    Id = Guid.NewGuid(), ConversationId = conversationId,
                                    Role = "assistant", Content = finalText, CreatedAt = DateTime.UtcNow,
                                });
                                conversation.UpdatedAt = DateTime.UtcNow;
                                await db.SaveChangesAsync();
                            }
                            await Sse("done", new { conversationId });
                            return;
                        }

                        // 有工具调用：assistant 消息（content + tool_calls）入上下文并落库
                        var toolCallsJson = JsonSerializer.Serialize(toolCallAcc.Values.Select(t => new
                        {
                            id = t.Id ?? $"call_{Guid.NewGuid():N}",
                            type = "function",
                            function = new { name = t.Name, arguments = t.Args.ToString() },
                        }).ToList());
                        messages.Add(new Dictionary<string, object?>
                        {
                            ["role"] = "assistant",
                            ["content"] = contentBuilder.ToString(),
                            ["tool_calls"] = JsonDocument.Parse(toolCallsJson).RootElement,
                        });
                        db.AiMessages.Add(new AiMessage
                        {
                            Id = Guid.NewGuid(), ConversationId = conversationId, Role = "assistant",
                            Content = contentBuilder.ToString(), ToolCallsJson = toolCallsJson,
                            CreatedAt = DateTime.UtcNow,
                        });
                        await db.SaveChangesAsync();

                        // 逐个执行
                        var callList = toolCallAcc.Values.ToList();
                        for (var i = 0; i < callList.Count; i++)
                        {
                            var callId = toolCallsJsonDeserializeId(toolCallsJson, i);
                            var toolName = callList[i].Name ?? "";
                            var argsRaw = callList[i].Args.ToString();
                            var args = string.IsNullOrWhiteSpace(argsRaw)
                                ? JsonDocument.Parse("{}").RootElement.Clone()
                                : JsonDocument.Parse(argsRaw).RootElement.Clone();
                            await Sse("tool", new { phase = "start", name = toolName });

                            string result;
                            var remote = remoteToolsCapture.FirstOrDefault(r =>
                                $"mcp_{r.ServerId.ToString("N")[..8]}_{r.Tool.Name}" == toolName);
                            if (remote != default)
                            {
                                try
                                {
                                    result = await mcp.CallToolAsync(remote.Url, remote.Auth, remote.Tool.Name, args, ct);
                                }
                                catch (Exception ex)
                                {
                                    result = JsonSerializer.Serialize(new { error = $"MCP call failed: {ex.Message}" });
                                }
                            }
                            else
                            {
                                result = await AiTools.ExecuteAsync(db, me, toolName, args, ct);
                            }
                            var resultTrim = result.Length > 4000 ? result[..4000] : result;
                            await Sse("tool", new { phase = "done", name = toolName, result = resultTrim[..Math.Min(600, resultTrim.Length)] });
                            messages.Add(new Dictionary<string, object?>
                            {
                                ["role"] = "tool",
                                ["tool_call_id"] = callId,
                                ["content"] = resultTrim,
                            });
                            db.AiMessages.Add(new AiMessage
                            {
                                Id = Guid.NewGuid(), ConversationId = conversationId, Role = "tool",
                                Content = resultTrim, ToolCallId = callId, ToolName = toolName,
                                CreatedAt = DateTime.UtcNow,
                            });
                        }
                        await db.SaveChangesAsync();
                        // 继续下一轮让模型消费工具结果
                    }
                    await SseError("tool rounds exceeded");
                }
                catch (Exception ex)
                {
                    await SseError(ex.Message);
                }
            }, "text/event-stream");
        }).AddEndpointFilter(SetupState.RequireInitialized);
    }

    private static string toolCallsJsonDeserializeId(string toolCallsJson, int index)
    {
        using var doc = JsonDocument.Parse(toolCallsJson);
        return doc.RootElement[index].GetProperty("id").GetString() ?? $"call_{index}";
    }

    /// <summary>消息对象压平为 OpenAI wire 格式（JsonElement 原样序列化）。</summary>
    private static Dictionary<string, object?> Compact(Dictionary<string, object?> msg) => msg;

    private static (string? Name, string? Icon, string? Prompt, string? Error) ValidateSkill(SaveSkillRequest r)
    {
        var name = r.Name?.Trim();
        var icon = string.IsNullOrWhiteSpace(r.Icon) ? "✨" : r.Icon.Trim();
        var prompt = r.Prompt?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 50) return (null, null, null, "name required (max 50)");
        if (icon.Length > 8) return (null, null, null, "icon too long");
        if (string.IsNullOrEmpty(prompt) || prompt.Length > 10_000) return (null, null, null, "prompt required (max 10000)");
        return (name, icon, prompt, null);
    }

    private static (string? Name, string? Url, string? Error) ValidateMcp(SaveMcpServerRequest r)
    {
        var name = r.Name?.Trim();
        var url = r.Url?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 50) return (null, null, "name required (max 50)");
        if (string.IsNullOrEmpty(url) || url.Length > 1000
            || !(url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, null, "http(s) url required");
        }
        return (name, url, null);
    }

    private static Guid CurrentUserId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue("sub");
        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("missing sub claim");
    }
}

public sealed record SaveProviderRequest(string? BaseUrl, string? ApiKey, string? Model);

public sealed record SaveSkillRequest(string? Name, string? Icon, string? Prompt);

public sealed record SaveMcpServerRequest(string? Name, string? Url, string? AuthHeader, bool? Enabled);

public sealed record ChatRequest(string? Content, Guid? SkillId);
