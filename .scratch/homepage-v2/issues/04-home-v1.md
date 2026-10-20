# 04: 新首页一期：今日卡+统计行+时间线并入

**What to build:** 新首页（替换旧 index hub）：今日卡（公历+农历+节气+实时天气，复用 02 的客户端天气模块，失败静默隐藏天气行）+ 四张统计数字卡（随记/日志/好友/连续记录天数；后端新增 GET /me/stats 返回 { totals, streak }）+ 旧「我的」时间线区块并入首页底部（带随记/日志筛选，复用 timeline 数据逻辑）。六 tab「首页」指到新页。**实现前先出 frontend-design 设计方案给主会话过目，再动代码。**

**Blocked by:** 02, 03

**Status:** ready-for-agent

- [ ] stats API（totals+streak）+ e2e
- [ ] 今日卡（农历节气天气，天气失败静默）
- [ ] 统计数字行四卡
- [ ] 时间线并入首页底部（带筛选）
- [ ] frontend-design 方案先行；build/tsc/playwright 全绿
