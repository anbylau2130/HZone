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

## 编辑器选型结论（2026-10-21，重要）

**uni-app H5 前端选第三方组件的铁律：绝不能依赖原版 `vue` 包。** uni-app 用分支 Vue（@dcloudio/uni-h5-vue），任何直依赖 vue 的组件库（md-editor-v3 等）都会双实例冲突挂载即崩（缺 Comment/EMPTY_ARR 导出，console 实锤）。专业编辑器定版 **CodeMirror 6**（框架无关，d494266）。同类候选先查 package.json 是否有 vue dependency。

## moments-wechat 批次（2026-10-21 完成）

- 01 签名/封面字段+端点（d28ad38，111/111）｜ 02 朋友圈 UI+profile 复用+个人资料区（b98847f，12/12）
- 随记页=朋友圈个人页：封面（可上传）+压角头像+签名档+日期分组流；好友主页同布局；ProfileHeader 公共组件
- 报告 .scratch/moments-wechat/acceptance.md；已知限制：默认封面渐变尾段硬编码暖色（待 token 化）

## prefs-sync（2026-10-21 完成）

- 6f3e8b1：主题/语言偏好账号级同步——PRD 6.9 欠账补齐。User.Preferences JSON 列（AddUserPrefs 迁移）；PUT /me/preferences（≤4KB）；/auth/me、/auth/login 下发；前端登录拉取覆盖本地、修改防抖 1s 上推、失败静默
- 终态：dotnet 116/116 + Playwright 14/14（新增多端同步旅程 2 条）
- **PRD 欠账清零**。剩余待讨论：dock 工具、日志页打磨

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

## drawio+mermaid 修复轮（2026-10-21，00d132e 之后的返修）

- 三根因（全部对着 cherry 0.11.11 dist 源码实锤）：
  1. **mermaid 不渲染**：addon 只认 window.mermaid / usePlugin 显式参 / src CDN，旧注释"自带渲染引擎"是误解。修法=官方 examples/mermaid.html 方案改离线：`pnpm add mermaid@11`（官方 demo 钉 11.x；12 未验证）+ `Cherry.usePlugin(插件, {mermaid, showSourceToolbar:true})`。**两个坑**：① 包默认入口 mermaid.core.mjs 有 d3/ts-dedent 外部依赖，uni vite 插件强制 preserveSymlinks:true 下 pnpm 解析不了→构建失败，必须走 `mermaid/dist/mermaid.esm.min.mjs`（零外部导入全量包）；② 该文件 render/initialize **只在 default 导出**（命名导出仅 5 个布局函数），namespace 导入会让 addon 静默判"找不到 mermaid"回退源码，必须 default 导入。阅读侧 detail.vue 不能实例化 addon（asyncRenderHandler 是 cherry 引擎内部机制，脱离 cherry 拿不到回填），直接 `Mermaid.render()` 异步换 DOM。
  2. **媒体插入真机打不开文件框**：cherry 原生 kO() 与自研菜单都用未挂 DOM 的 input 直接 .click()——iOS Safari/微信 WebView 静默失败。修法：input 先 `position:fixed;left:-9999px` 挂 body 再 click（display:none 同样不可点），settle 后 remove；audio 也换成自研 InsertAudioMenu（原生 audio 走 kO 同病），语法对齐官方 `!audio[name](url)`。
  3. **drawIO**：旧 blob: 桥接依赖 embed.diagrams.net（国内不可靠）。修法=官方 examples/drawio_demo.html 自托管方案：drawio_lib+mxgraph 静态资源（199 文件 6.4MB，源自 cherry 仓库 examples/assets，跳过运行时不加载的 src/js；tencent.github.io 镜像逐文件下载）落 `frontend/src/static/drawio/`，`drawioIframeUrl: '/static/drawio/index.html'`，drawio-bridge.js 照抄官方协议（ready/setData/getData:getData:success）。**产物必须保持官方 base64 dataURL 形态**：cherry"预览区点击图片再编辑"的 tQ 正则只认 `data:image/..;base64,`，换 /media 路径会静默失去再编辑（此前 /media 直传方案的真实缺陷）。工具栏补上被误裁的 drawIo 按钮；撤销 `.cherry-dialog--sure` 隐藏（getData 回路依赖确定按钮）。exportImage(editable=true) 会把 xml 嵌 PNG zTXt 块，drawio 原生可再开。
- 终态：Playwright 17/17（cherry spec 新增：音频插入、graph 模板 mermaid 渲染断言 figure svg、drawio 全链路=弹层+editorUIInstance 就绪+setData 注入 g rect+确定导出 dataURL 插入+预览 img[data-type=drawio]）；tsc 0 错；build:h5 通过（dist 含 static/drawio 与 mermaid chunk）
- 踩坑：e2e 里 drawio 插入前先 Ctrl+End 逃出代码围栏（mermaid 模板测试的光标留在围栏内，直插会被吞成代码文本）；curl 拉镜像文件名中文需 encodeURIComponent；TSV 末行无换行会被 while read 静默跳过

## 浏览器实测轮（2026-10-22，用户指令：e2e+浏览器自测+修复到全绿+推送 GitHub）

- 新增 browser-tour.spec（第 18 条）：UI 全链路=发布日志（图/音/视/mermaid/drawio）→发布→详情页渲染断言+逐步截图；截图经视觉模型目检
- **连环挖出 4 个真 bug 并即修**：
  1. **连续插入互吞**（最重要）：cherry fire() 异步回填后把刚插入的文本设为选区（cacheOnce 二次点击设计），下一笔插入会把"仍被选中的上一笔"整段替换——图→音→视→流程图连插只剩最后一笔（bisect 实测实锤）。修：InsertMediaMenu 注册 afterClickCb 把选区折叠成光标。旧 cherry spec 没查前序内容所以漏网
  2. **详情页 /media 全 401 断图**：detail.vue v-html 直出，img/video/audio 带不上 Authorization（GET /media 是 [Authorize]）。修：mdHtml watch 后 loadMediaUrl 换授权 blob src（与编辑器 swapMediaSrc 同模式）
  3. **详情页 drawio 属性泄漏**：marked 不认 cherry `{data-type=drawio data-xml=..}` 属性块，泄漏成正文文本。修：预处理正则转 `<img data-type="drawio">`；顺带 audio 转换补 controls（此前 audio 无控件=隐形）
  4. **详情页媒体溢出**：.md-body 无 max-width 约束，大图横向撑破卡片（截图目检发现）。修：:deep(img/video) max-width:100%
- spec 教训：cherry spec 中段有 page.reload()（预览开关持久化验证）会清空草稿，"前序内容存活"断言必须放 reload 之前
- 终态：**Playwright 18/18** + tsc 0 错 + build:h5 通过；浏览器 tour 详情页断言含"图片真实解码"（naturalWidth>0，防元素在但 401）

## 编辑=创建同页轮（2026-10-22，用户指令：日志编辑复用创建页）

- 旧态：日志编辑是 detail 页内联裸 textarea（detail?edit=1），与创建页（cherry 全套编辑器）割裂
- 改法：
  - publish.vue 支持 `?id=`：loadForEdit 回填标题/正文/天气快照/收件人/say 媒体（/media 预览换授权 blob）；日记头还原原发布日期+农历（editLunar）而非今天；submit 分流 create/update（后端 PUT 本就支持 mediaIds/recipientIds/weather 整体替换，前端 wrapper 此前只放了 text/title/markdownBody——扩了载荷类型）；按钮/导航栏文案切"保存/编辑内容"（新增 i18n hub.edit/publish.saved）
  - detail.vue：日志 startEdit → navigateTo publish?id&kind=journal；随记保留内联短文本编辑；加 onShow(loadedOnce) 刷新——编辑保存 navigateBack 返回时 onLoad 不重跑，不刷则停留在旧正文
  - ContentCard/moments goEdit 按 kind 分流：日志直达 publish，随记仍 detail?edit=1
  - say 媒体编辑语义 = 整体重绑（移除格子=解绑，保留项原 mediaId 原样提交），与后端 PUT 语义一致
- e2e：新增 edit-journal.spec（第 19 条）：API 造日志→detail 点✎→断言落 publish 页+cherry 回填→改标题/正文→保存→detail 自动刷新+API 复核落库
- 浏览器实测：用户本人在内置浏览器实操编辑页（playwright MCP 快照恰好截到）：编辑内容标题/原日期农历天气/cherry 全工具栏/原 drawio 图回填/保存按钮全部正确
- console 噪音定性（不修）：/media 401 = blob 换取前的瞬态请求（swapMediaSrc/observer 已兜住）；mermaid usePlugin 警告 = 仅 HMR 重执行模块时旧实例在场，产线路由不受影响
- 终态：**Playwright 19/19** + tsc 0 错

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
