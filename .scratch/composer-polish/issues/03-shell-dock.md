# 03: 桌面三栏壳 + 右侧工具 dock

**What to build:** 桌面 ≥768px 三栏：左导航 220px ｜ 中间内容加宽（moments/received/timeline/detail 42→48rem；home 60rem；journal 编辑器 56rem）｜ 右侧工具 dock ~300px（仅 ≥1200px 显示）：「今日」小卡（农历+天气，复用 /me/stats.today+getWeather），其余工具位留注释占位（音乐播放器等后续讨论）。

**Blocked by:** None

**Status:** done

- [x] 三栏壳（dock ≥1200px 显示；768–1199 与移动端零变化）
- [x] 内容列加宽（48rem 等）
- [x] dock 今日小卡（农历+天气）
- [x] build/tsc/playwright 12/12 全绿（a85d480；修复 1280 下内容伸入 dock 底下的溢出）
