# 01: 农历+天气保存链路（后端）

**What to build:** 发布链路支持记录"何时+何地"。发布/编辑 API 接受可选天气快照 { city, condition, tempC }；后端用 System.Globalization.ChineseLunisolarCalendar 在保存时计算并落库农历字段（干支年+农历月日+节气，节气用简化日期表）；详情/列表 DTO 携带农历+天气新字段。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [x] Say/Journal 实体加天气快照与农历字段；发布/编辑 API 接受天气快照；DTO 携带新字段
- [x] 农历计算抽纯逻辑类+单测（已知公历日期断言干支年/农历月日/节气）
- [x] e2e：发布带天气快照→DTO 含快照与农历；不带天气→农历仍落库、天气为空
