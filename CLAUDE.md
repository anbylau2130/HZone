# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
