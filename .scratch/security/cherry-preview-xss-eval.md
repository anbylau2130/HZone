# cherry previewOnly XSS 面评估（2026-10-24）

**结论：有效防护，评估通过。** cherry 0.11.11 的预览管线（esm 包内联 dompurify）
对他人内容的渲染面无脚本执行、无事件属性注入、无危险协议链接。

## 背景

- 渲染面：ContentCard/阅读流/收到/搜索/个人页读侧全部走 CherryEditor `previewOnly`，
  渲染**他人**提交的 markdownBody —— 是跨用户 XSS 主战场
- cherry 的 sanitize 链路（dist 包逆向确认）：
  - 原生 HTML 块（htmlBlock）→ `sanitizer.sanitize($str, config)`（内联 dompurify）
  - `sanitizeText` → `purify.sanitize(..., { FORBID_TAGS: ["style"] })`
  - 全局 `afterSanitizeAttributes` 钩子处理 href/src
- **dompurify 是 cherry 打包产物内联依赖**，升级需随 cherry 版本走

## 实测矩阵（headless Chromium 390px，经首页卡片 previewOnly 渲染）

载荷全文提交为一篇日志（已删除），渲染后断言：

| 载荷 | 结果 |
| --- | --- |
| `<script>...</script>` | 转义为可见文本，0 执行 |
| `<img src=x onerror=...>` ×2 | img 保留、onerror/onload 剥离（惰性） |
| `[md link](javascript:...)` | 无 javascript: href 进 DOM |
| `<a href="javascript:...">` | a 保留、危险 href 剥除 |
| `<details open ontoggle=...>` | details 保留、ontoggle 剥离 |
| `<svg onload=...>` | svg 保留（惰性）、onload 剥离 |
| `<iframe src="javascript:...">` | 转义为可见文本 |
| `<a href="data:text/html,...">` | 转义为可见文本 |
| mXSS（form/math/mtext/style 组合） | 整体转义为文本 |

断言数据：11 个 `window.__xss_*` 执行标记全 null；预览 DOM 0 script、0 iframe、
0 style、0 个 `on*` 属性、0 个 javascript:/data: href。

## 遗留（非漏洞，记录在案）

- cherry 预览区 `<img src="/media/...">` 首渲染带裸 src：浏览器先发一次无鉴权
  请求得 401，MutationObserver 随后换授权 blob（显示正确，代价是每图挂载一次
  401 请求 + API 日志噪音）。cherry image 语法无渲染钩子（仅 selfClosing 配置），
  配置级不可修；如需根治需自定义 image 语法渲染器（深入 cherry 内部，暂不做）
- sanitize 质量随 cherry 版本锁定，升级 cherry 时应重跑本矩阵
  （脚本：`e2e/` 下 `xss-eval.mjs` 模式，归档于 `.playwright-mcp/xss-eval.mjs`）
