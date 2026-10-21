# 02: 随记页朋友圈化 UI + 好友主页同步 + 设置页个人资料区

**What to build:** moments 页重构：顶部背景图（高 ~300px，默认渐变，点击上传封面）+ 头像压角（AvatarCircle 72px，下缘越出背景）+ 签名档；下方内容流按中国区日期倒序分组（左列：今天/昨天/M月/yyyy年M月；右侧正文+媒体九宫格+天气 chip；编辑/删除图标保留）。profile 页同布局复用（好友的封面/头像/签名+可见内容流）。设置页「个人资料」区：签名编辑+封面上传/移除（与头像行整合）。i18n 对齐；playwright 全绿。

**Blocked by:** 01

**Status:** ready-for-agent

- [x] moments 朋友圈布局（封面+压角头像+签名+日期分组流）
- [x] profile 页同布局复用
- [x] 设置页个人资料区（签名/封面编辑）
- [x] build/tsc/playwright 全绿
