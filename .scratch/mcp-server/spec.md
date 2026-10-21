# Spec: HZone MCP Server（mcp-server）

**Status:** ready-for-agent ｜ 来源：用户批准的提案（stdio + PAT，9 工具）2026-10-21

## 决策

- **形态**：stdio MCP server，Node/TypeScript，`@modelcontextprotocol/sdk` 官方包，放仓库 `mcp/` 目录；构建产物 `mcp/dist/index.js`
- **配置**：环境变量 `HZONE_URL`（默认 http://localhost:5000）+ `HZONE_PAT`（设置页生成的个人令牌，hz_ 前缀）。server 用 PAT 调 HZone REST API——**权限=本人，可见名单规则天然生效**
- **工具（9 个）**：
  | 工具 | 参数 | 对应 API |
  |---|---|---|
  | hzone_list_contents | kind?/query?/offset?/limit? | GET /me/contents 或 /search |
  | hzone_get_content | id | GET /contents/{id}（含评论） |
  | hzone_search | q/offset?/limit? | GET /search |
  | hzone_list_received | kind?/offset?/limit? | GET /me/received |
  | hzone_inbox | 无 | GET /inbox（摘要+未读数） |
  | hzone_create_say | text/recipientIds?/weather?{city,condition,tempC} | POST /contents/says |
  | hzone_create_journal | title/markdown/recipientIds? | POST /contents/journals |
  | hzone_comment | contentId/text | POST /contents/{id}/comments |
  | hzone_friends | 无 | GET /friends |
- **不做**：删除/编辑（agent 误操作风险）；头像/设置类；媒体上传（后续）
- **健壮性**：PAT 未配置→启动即报错退出并提示；API 401→工具返回明确错误文案（令牌失效请重新生成）；网络错误→友好错误

## Out of Scope

删除/编辑工具、媒体上传工具、SSE/HTTP transport（stdio 够用）、资源订阅。