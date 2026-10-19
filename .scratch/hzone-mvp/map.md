# HZone MVP 施工档案（Ralph 循环进度）

> 每轮迭代开始时先读本文件 + `git log`，继续 frontier 上的 ticket；完成后更新本文件并提交。

## 状态速览

- **Ticket 01 ✅** 解决方案骨架 + /health + WAF e2e 模式（commit 66cd602）
- **Ticket 02 ✅** 领域实体+EF Core+可见名单单一入口+JWT 管线+可见性 e2e（commit 09e9ce2，5/5 绿）
- **Ticket 03 ✅** 初始化向导+系统配置标记+未初始化 409 防护（commit 675db97，10/10 绿）
- **Ticket 04 ✅** 邀请码/注册/邮箱验证/登录/密码重置+FileEmailSender（commit 5b67f33，18/18 绿）
- **Ticket 05 ✅** 双向好友 API+申请站内信扩展（commit ca0bc05，25/25 绿）
- **Ticket 12 ✅(代码)** 前端认证旅程+api client+auth store+i18n+proxy rewrite（commit 5404518，build 通过；Playwright 冒烟延后到 13/15）
- **Ticket 06 ✅** 媒体上传(ImageSharp 压缩+缩略图)/本地存储/说说媒体绑定/编辑/硬删除级联（commit 79ddc67，35/35 绿）
- **Ticket 07 ✅** 列表(ContentQuerySpec 单一入口)/received/主页/搜索/详情 DTO（commit 58e396c，42/42 绿；t07 代理停滞已由 t07b 接手完成）
- **Ticket 08 ✅** 评论+站内信（mention/comment/friendRequest + 已读；发布时写 Mentioned 行）（commit e8d9d22，50/50 绿）
- **Ticket 09 ✅** PAT 生成/吊销/列表 + PatAuthHandler（hz_ 前缀分流 scheme）（commit 441f48f，58/58 绿）
- **Ticket 10 ✅** 整站 zip 导出/导入 + 批量 @（默认私密导入）（commit 2d06a63，65/65 绿）
- **Ticket 11 ✅** 管理员配置/用户禁用/邀请开关/ffmpeg 压缩决策单测（commit 6571408，83/83 绿）
- **Ticket 13 ✅(代码)** 前端内容旅程 13 页面（commit 2c778a8，build 通过；Playwright 旅程在 15 做）
- **Ticket 14 ✅** 主题系统（预置4套+自定义+对比度校验+本地存储）+设置页+本地化补漏（commit 2d71fd7，build 通过）
- **Ticket 15 ✅** 整体验收（commit fbdb550）：API e2e 87/87 + Playwright UI 6/6（本地 :5000+:5173，无 Docker）+ acceptance.md 28 条用户故事全覆盖
- **MVP 完成**（2026-10-20）。验收中发现并跨栈修复 4 个旅程阻断 bug（好友页模板崩溃/@i18n转义/邮件链接路由/launchSettings 端口）。
- 遗留：主题账号级跨端同步未做（本地存储，acceptance.md 已标注）；UI e2e 仅 H5（按用户要求）

## 一键验收

```bash
dotnet test                              # 87 API e2e
cd e2e && npx playwright test            # 6 UI 旅程（自动起 API+H5）
# 或 ./run-mvp-tests.ps1
```

## 运行

```bash
cd src/HZone.Api && dotnet run           # API :5000（首次访问 /setup/status 走向导）
cd frontend && pnpm dev:h5               # H5 :5173（/api 代理 :5000）
```
- Ticket 03~15：待办（依赖顺序见 issues/*.md 的 Blocked by）
- 前端：uni-app(Vue3) 脚手架已建（frontend/，npm 走 npmmirror，@dcloudio 版本 3.0.0-5020620260917001，vite 5.2.8，vue-i18n+pinia 已装），H5 dev 验证待做
- 环境注意：
  - GitHub 直连失败（degit/clone TLS error）→ npm 走 npmmirror registry
  - 主会话直接 Write 长文件会随机乱码 → 优先委托 executor 子代理写代码，用 dotnet test 兜底
  - SDK 11 RC 装过但 global.json 锁 8.0.426，TFM 必须 net8.0，模板生成的 MapOpenApi（.NET 9 API）不可用
  - 解决方案文件是 HZone.sln

## 验证口径

- 后端：`dotnet test`（WebApplicationFactory 进程内 e2e，HTTP 边界 seam）
- 前端 H5：`pnpm -C frontend dev:h5`（:5173，/api 代理到 :5000）+ Playwright（ticket 15）
- 全部本地，无 Docker
