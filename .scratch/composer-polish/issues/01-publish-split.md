# 01: 发布页去切换器 + 随记媒体缩略图分离

**What to build:** 发布页移除顶部随记/日志切换器（kind 由入口 query 决定并全程固定，日记头保留）；随记媒体缩略图移到输入区下方独立区块（与输入框视觉分离）。

**Blocked by:** None

**Status:** done

- [x] 切换器移除，kind 固定由 query 决定（无 query 默认 say）
- [x] 媒体缩略图独立区块（输入框下方）
- [x] build/tsc/playwright 全绿（c620e39，12/12 独立复核）
