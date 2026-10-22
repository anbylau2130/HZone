# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.


## Commands

```bash
dotnet test                              # 后端全量 e2e（87 条，进程内 HTTP，WebApplicationFactory）
cd e2e && npx playwright test            # UI e2e（6 旅程，自动起 API :5000 + H5 :5173，全本地无 Docker）
./run-mvp-tests.ps1                      # 一键全套
cd src/HZone.Api && dotnet run           # API :5000；首次访问 /setup/status 走初始化向导
cd frontend && pnpm dev:h5               # H5 :5173（/api 代理 :5000）
cd frontend && pnpm build:h5             # H5 构建校验
```

## Architecture

.NET 8 最小 API（src/HZone.Api）+ Core（实体/接口）+ Infrastructure（EF Core SQLite，部署目标库 SqlServer/PG/MySQL 经 EF 切换）。
前端 uni-app(Vue3+TS) H5，design token 主题（--hz-* CSS 变量），vue-i18n 中英双语。

**可见名单单一入口**：所有内容读取走 `IContentQuery`/`ContentQuerySpec`（Infrastructure/ContentQuery.cs）——界面与 PAT API 同口径；不可见一律 404 防存在性泄漏。修改内容可见性语义时改这里，不要在端点里另写权限判断。

**认证**：JWT Bearer（邮箱登录）+ PAT（hz_ 前缀 Bearer）双 scheme 分流；被禁用用户在 token 校验时查库拒绝。

**通知**：零推送。事件（@/评论/好友申请）只写 InboxMessage 行；前端 60s 轮询 unread-count。

**测试**：HTTP 边界是唯一后端测试 seam；测试配置覆盖必须用 `UseSetting`（minimal hosting 下 AddInMemoryCollection 不生效）。

## Issue tracker

`.scratch/hzone-mvp/`：spec.md + issues/01-15 + map.md（施工档案）+ acceptance.md（MVP 验收报告，28 条用户故事证据）。新工作开新 feature 目录。

## 工具链注意

- global.json 锁 SDK 8.0.426，TFM net8.0；TFM 必须 net8.0（模板可能生成 net11）
- GitHub 直连不通，npm 走 npmmirror；@intlify/* 用 pnpm.overrides 钉 9.14.x（vue-i18n 必须 ^9 不能 v10）
- 主会话直接写长文件会随机乱码 → 代码实现委托 executor 子代理，`dotnet test`/`pnpm build:h5` 兜底
- **uni-app 选第三方组件铁律**：绝不能依赖原版 `vue` 包（uni 用分支 Vue，双实例冲突挂载即崩）——选型先查 package.json；框架无关的（如 CodeMirror 6）安全
- **pnpm + uni 的 vite resolver 不穿透符号链接**：transitive 依赖（@codemirror/*、@lezer/* 等）必须显式声明为直接依赖，否则构建 504/无法解析

## 代码查询工具规定

- 查代码一律用 **codegraph**（符号定位/调用关系/影响面）和 **graphify**（架构与知识图谱），不要用 grep 全文搜索代码。
- 仅当两者都答不了（如查任意字符串/日志文本）才退回文本搜索。
