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
- **后 MVP 改版**（commit 4aa3154 + 39d09ca）：「说说」→「随记」（GLOSSARY/PRD/i18n 全站）；随记/日志分页（moments/journals）+ 自定义 tabBar（首页/随记/日志/收到/我的）；QQ空间温情版视觉（蜜杏暖阳/暖夜 token、masthead 首页）。实现决策：① 蜜黄 #E9A23B 对 surface 对比度 2.13 达不到 ≥3（物理不可达），App.vue 基础色照方案，4 套预置主题 primary 用加深变体实测达标（#B8791F 等）；② uni-app H5 hash 直载时 onLoad 不稳，列表页用 onShow 刷新。
- **缺口补齐**：前端初始化向导页（238e0f8，后端接口早有但页面缺失——ticket 12/13 的真实缺口，Playwright 未覆盖"全新实例首访"路径）。
- 遗留：主题账号级跨端同步未做（本地存储，acceptance.md 已标注）；UI e2e 仅 H5（按用户要求）

## homepage-v2 批次（2026-10-20 完成）

- 01 农历+天气保存链路（15f4ec5）｜ 02 发布页天气 UI（ebc03e5）｜ 03 六 tab 导航重组（7482573）｜ 04 新首页一期（899f6f4）｜ 05 热力格+足迹（8e62213）｜ 06 首页三期四模块（229374a）｜ 07 收口验收（本次）
- 终态：dotnet test 103/103 + Playwright 12/12 全绿；验收报告 .scratch/homepage-v2/acceptance.md
- 领域决策：天气快照（城市+现象+气温）+ 农历（干支年/月日/节气，ChineseLunisolarCalendar+寿星公式±1天）落库；定位/天气全客户端化（ipwho.is→Open-Meteo，禁服务端代查）；旧内容农历 createdAt 惰性推导
- 导航：六 tab 首页/随记/日志/收到/主题/设置；搜索上移；我的时间线并入首页；设置页=hub 入口、主题页=外观定制
- EnsureCreated 不加列的教训与 EF Migrations 待办已记（见上）

## moments-wechat 批次（2026-10-21 完成）

- 01 签名/封面字段+端点（d28ad38，111/111）｜ 02 朋友圈 UI+profile 复用+个人资料区（b98847f，12/12）
- 随记页=朋友圈个人页：封面（可上传）+压角头像+签名档+日期分组流；好友主页同布局；ProfileHeader 公共组件
- 报告 .scratch/moments-wechat/acceptance.md；已知限制：默认封面渐变尾段硬编码暖色（待 token 化）

## mcp-server 批次（2026-10-21 完成）

- 01 stdio server+9 工具（7d7a1ed）｜ 02 README+验收（本次）；同批次修复日志编辑器/上传（05b91e4，t35 调试代理）
- 终态：dotnet 111/111 + Playwright 12/12；MCP 端到端亲测（真实 PAT：9 工具/读/写/吊销）；报告 .scratch/mcp-server/acceptance.md
- 接入：见 mcp/README.md（Claude Code/Desktop 配置，HZONE_URL+HZONE_PAT）
- 待办队列：随记/日志页重设计剩余项（日志页已修，需再验收）、dock 工具、主题账号级同步

## infra：EF Migrations（2026-10-21 完成）

- commit c6e5994：EnsureCreated → Migrate()；InitialCreate 迁移入 git；legacy 库（有表无 history）自动 baseline 后增量；dev 库已重建走新链路
- 今后 schema 变更：`dotnet ef migrations add X -p src/HZone.Infrastructure -s src/HZone.Api`，启动自动应用
- 待办队列：MCP server（方案已提报待批：stdio + PAT，9 工具读写）；随记/日志两页重设计（待讨论）；dock 工具（待讨论）；主题账号级同步

## composer-polish 批次（2026-10-21 完成）

- 01/02 发布页（c620e39）｜ 03 三栏壳+dock（a85d480）｜ 04 朋友圈化（fffc3eb）｜ 05 头像（ef11be9）｜ 06 收口（本次）
- 终态：dotnet 106/106 + Playwright 12/12；报告 .scratch/composer-polish/acceptance.md
- 新结构：桌面三栏（侧栏 220px｜内容 48-60rem｜右 ToolDock 300px ≥1200px）；发布页日记头+整体卡片+按住录视频；日志编辑器上下布局+MD 附件插入；朋友圈化随记卡；头像全栈
- 流程教训：**建票前先确认 cwd**（本批次 ticket 文件曾写进 e2e/.scratch/ 被误删，git 未收录，已重建）

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

**开发库 Schema 变更注意**：EnsureCreated 不给已存在的库加列——后端加字段后要么删 `src/HZone.Api/data/hzone.db` 重建，要么在线 ALTER TABLE 补列（t20 曾用后者救场）。正式方案是切到 EF Migrations（待办）。

- 后端：`dotnet test`（WebApplicationFactory 进程内 e2e，HTTP 边界 seam）
- 前端 H5：`pnpm -C frontend dev:h5`（:5173，/api 代理到 :5000）+ Playwright（ticket 15）
- 全部本地，无 Docker
