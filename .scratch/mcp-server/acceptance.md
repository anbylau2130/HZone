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

- 05b91e4：日志编辑器 markdown 体验与媒体上传。根因实锤：① .md-preview 在 <768px display:none——手机上只有裸 textarea 无渲染反馈（"不像 markdown 编辑器"的真因）；② 上传走 blob: 地址喂 uni.uploadFile，其 urlToFile 靠 XHR 拉 blob: URL，是浏览器兼容最脆弱一环（部分环境静默失败）。修复：预览全宽度显示（手机 30vh 滚动）；uploadFile 放宽收 Blob 走 fetch+FormData 直传；失败双层 toast
- 追加收敛（进行中→见下）：avatar/cover 上传与 zip 导入三处同链路改 fetch 直传

## 已知限制

- 无删除/编辑工具（防 agent 误操作）；媒体上传工具后续
- stdio only
