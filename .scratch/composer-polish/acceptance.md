# composer-polish 批次验收报告

**日期：** 2026-10-21 ｜ **结论：** 全部通过（6/6 tickets）

- 进程内 API e2e：**106/106 绿**（103 + 头像 3 条）
- Playwright UI e2e：**12/12 绿**（2026-10-21 主会话独立复核）
- 一键验收：`./run-mvp-tests.ps1`（计数 103/12 已是最新口径）

## 交付对照（spec 逐条证据）

- **01 发布页去切换器+媒体分离**（c620e39）：切换器移除（kind 由 query 固定）；媒体缩略图独立区块
- **02 日志编辑器**（c620e39）：标题独立无圆角、上下布局（编辑上/预览下）、工具栏（图片/视频上传插入光标处、加粗、标题）
- **03 三栏壳+右 dock**（a85d480）：≥1200px 右侧 300px ToolDock（今日小卡：农历+天气，工具位注释占位）；内容列加宽 48rem/60rem/56rem；修复 1280 下内容伸入 dock 底下的溢出（margin-right 预留）
- **04 朋友圈化**（fffc3eb）：ContentCard 头像+昵称+正文+九宫格+日期行，编辑/删除图标化（CardOwnerActions，仅作者）；detail 同步
- **05 头像**（ef11be9）：User.AvatarUrl + POST/DELETE /me/avatar（复用压缩管线）；/auth/me、/friends、内容 author DTO 下发；设置页 72px 头像行；AvatarCircle 全站（侧栏/ContentCard/moments），无头像回落首字圆；dev 库在线 ALTER TABLE 补列
- **日记头+按住录视频**（f189dcc，前序）：日记头（公历+农历+节气｜城市天气可改）；整体卡片；MediaRecorder 按住录制松开预览

## 批次中处理的问题

- **ticket 文件丢失事故**：建票时会话 cwd 在 e2e/ 下，票写进了 e2e/.scratch/ 后被清理，git 只有 spec.md——已重建 01-06（本目录 issues/），并把"建票前先确认 cwd"记入流程注意
- dev server 锁 DLL / EnsureCreated 不加列两个老问题再次出现，均按既有预案处理（taskkill+重启；在线 ALTER TABLE）

## 已知限制

- dock 工具位只有注释占位（音乐播放器等后续讨论）
- 旧头像文件保留为孤儿媒体（不清理，已知简化）
