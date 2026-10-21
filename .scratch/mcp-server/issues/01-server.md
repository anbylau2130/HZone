# 01: stdio MCP server 与 9 工具

**What to build:** `mcp/` 目录：package.json（@modelcontextprotocol/sdk）+ tsconfig + src/index.ts（stdio server，env HZONE_URL/HZONE_PAT，PAT 缺失启动报错退出；9 个工具按 spec 表实现，走 REST API；错误文案友好——401 提示令牌失效、网络错误提示检查 HZONE_URL）。构建 `pnpm build`（tsc → dist/index.js）。自测脚本 scripts/smoke.mjs：spawn dist/index.js 发 JSON-RPC（initialize→tools/list→tools/call hzone_inbox 与 hzone_create_say 真实调用本地实例），断言响应。

**Blocked by:** None

**Status:** ready-for-agent

- [x] server + 9 工具实现
- [x] smoke 脚本对本地实例跑通（读+写各至少一次真实调用）
- [x] pnpm build + tsc 通过
