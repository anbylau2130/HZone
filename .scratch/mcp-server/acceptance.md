# mcp-server 批次验收报告

**日期：** 2026-10-21 ｜ **结论：** 全部通过（2/2 tickets）

- 回归：dotnet **111/111** + Playwright **12/12** + build/tsc（主会话独立复核）
- **MCP 端到端亲测**（主会话以真实 PAT 走全流程）：
  - 设置页 PAT 生成 → stdio 握手 initialize → tools/list 返回 9 工具 → `hzone_inbox` 真实读（unreadCount=2）→ `hzone_create_say` 真实写（内容落库有 id）→ 测试 PAT 吊销（后续调用将 401）
  - 冒烟脚本 `mcp/scripts/smoke.mjs` 可重复执行

## 交付对照

- **01 server**（7d7a1ed）：mcp/ 目录（TS + @modelcontextprotocol/sdk，stdio），env HZONE_URL/HZONE_PAT（缺失启动报错退出）；9 工具（列表/详情/搜索/收到/站内信/发随记/发日志/评论/好友）；401→「令牌失效请重新生成」、网络错→「无法连接」友好文案；stdout 协议线干净；smoke 脚本随仓
- **02 文档**（本次）：mcp/README.md——构建步骤、Claude Code/Desktop 配置示例、PAT 生成指引、工具清单、故障排查、已知限制

## 同批次修复（用户报告）

- 05b91e4：日志编辑器 markdown 体验与媒体上传（t35 调试代理定位：H5 文件选择手势链路 + blob: 路径喂 uni.uploadFile 不可靠 + 光标插入失效；改 fetch 直传 File、失败必 toast、原生 selection 插入；12/12 回归绿）

## 已知限制

- 无删除/编辑工具（防 agent 误操作）；媒体上传工具后续
- stdio only
