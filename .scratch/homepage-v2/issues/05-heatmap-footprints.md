# 05: 新首页二期：热力格+足迹

**What to build:** 热力格：近 90 天每天发布篇数着色方格阵（按周列排布），深浅 5 档，点格跳转到筛选当天的我的时间线。足迹：城市按时间顺序时间线（杭州→上海→…），**同城连续去重**（一直同城只显示一条），数据源=内容天气快照的 city；无城市数据不显示该模块。stats API 扩展 { heatmap, footprints }。设计语言延续 04 的 frontend-design 方案。

**Blocked by:** 04

**Status:** ready-for-agent

- [ ] 热力格组件 + stats API heatmap（点格跳转当天）
- [ ] 足迹时间线（同城连续去重）+ stats API footprints
- [ ] e2e：heatmap/footprints 正确性与去重断言（多城市种子数据）
