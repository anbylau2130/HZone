# HZone MCP Server

让 Claude（Claude Code / Claude Desktop）通过 MCP 直接读写你的 HZone——权限等于你本人（PAT = 你的身份，可见名单规则天然生效：agent 只能看到你能看到的）。

## 构建

```bash
cd mcp
pnpm install
pnpm build   # 产物 dist/index.js
```

## 配置

先在 HZone **设置页 → PAT 管理**生成个人访问令牌（明文只显示一次），然后二选一：

**Claude Code**（项目 .mcp.json 或全局）：

```json
{
  "mcpServers": {
    "hzone": {
      "command": "node",
      "args": ["<仓库路径>/mcp/dist/index.js"],
      "env": {
        "HZONE_URL": "http://localhost:5000",
        "HZONE_PAT": "hz_xxxxxxxxxxxx"
      }
    }
  }
}
```

**Claude Desktop**（claude_desktop_config.json）同样结构：mcpServers.hzone 的 command/args/env 与上例一致。

## 工具清单（9 个）

| 工具 | 说明 | 关键参数 |
|---|---|---|
| hzone_list_contents | 列我的内容 | kind / query / offset / limit |
| hzone_get_content | 看一篇（含评论） | id |
| hzone_search | 搜我的内容 | q / offset / limit |
| hzone_list_received | 我被 @ 的内容 | kind / offset / limit |
| hzone_inbox | 站内信摘要+未读数 | 无 |
| hzone_create_say | 发随记 | text / recipientIds / weather{city,condition,tempC} |
| hzone_create_journal | 发日志 | title / markdown / recipientIds |
| hzone_comment | 评论可见内容 | contentId / text |
| hzone_friends | 好友列表（@ 选人用） | 无 |

## 故障排查

| 现象 | 处理 |
|---|---|
| 启动报「HZONE_PAT 未配置」 | 设置页 → PAT 管理 → 生成，填入 env |
| 工具报「令牌无效或已吊销」 | 设置页重新生成 PAT 并更新配置 |
| 「无法连接 HZone」 | 确认 API 在跑（默认 :5000），HZONE_URL 正确 |

## 已知限制

- 不提供删除/编辑工具（防 agent 误操作）
- 媒体上传工具后续再加
- stdio only；需要远程共享再议 SSE/HTTP transport
