namespace HZone.Core.Entities;

/// <summary>
/// AI 对话会话（多会话管理）：标题首次从用户消息截取；消息独立成表。
/// </summary>
public class AiConversation
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Title { get; set; } = "新对话";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public List<AiMessage> Messages { get; set; } = [];
}

/// <summary>
/// 对话消息：user/assistant/tool 三态。assistant 的工具调用请求存 ToolCallsJson
/// （OpenAI 格式 [{id,name,arguments}]）；tool 消息的 ToolCallId 回指。
/// </summary>
public class AiMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>user | assistant | tool。</summary>
    public string Role { get; set; } = "user";

    public string Content { get; set; } = "";

    /// <summary>assistant 发起的工具调用（OpenAI tool_calls JSON）。</summary>
    public string? ToolCallsJson { get; set; }

    /// <summary>tool 消息回指的调用 id。</summary>
    public string? ToolCallId { get; set; }

    /// <summary>工具展示名（tool 消息冗余存，便于前端渲染“🔧 搜索笔记”）。</summary>
    public string? ToolName { get; set; }

    public DateTime CreatedAt { get; set; }

    public AiConversation Conversation { get; set; } = null!;
}

/// <summary>AI 模型配置（每用户一条）：OpenAI 兼容协议。</summary>
public class AiProviderSetting
{
    public Guid UserId { get; set; }

    /// <summary>OpenAI 兼容 BaseUrl（如 https://api.z.ai/api/paas/v4）。</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>API Key（只写不回显；响应只带 hasKey）。</summary>
    public string? ApiKey { get; set; }

    /// <summary>模型名（如 glm-4.7）。</summary>
    public string Model { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>AI 技能（预设指令模板）：对话发送时套用为 system 指令。</summary>
public class AiSkill
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>emoji 图标。</summary>
    public string Icon { get; set; } = "✨";

    /// <summary>system 指令正文。</summary>
    public string Prompt { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}

/// <summary>外部 MCP server（HTTP JSON-RPC）：工具注入 AI 对话。</summary>
public class AiMcpServer
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>Streamable HTTP endpoint（POST JSON-RPC）。</summary>
    public string Url { get; set; } = null!;

    /// <summary>可选鉴权头（如 "Authorization: Bearer xxx"，原文一行）。</summary>
    public string? AuthHeader { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
