# moments-wechat 批次验收报告

**日期：** 2026-10-21 ｜ **结论：** 全部通过（2/2 tickets）

- 进程内 API e2e：**111/111 绿**（106 + 签名/封面 5 条）
- Playwright UI e2e：**12/12 绿**（主会话独立复核）
- 浏览器实测（1440 桌面）：朋友圈布局完整呈现——封面区（t33 测试上传的真实图片）+ 头像压角右下 + 签名档 + 日期分组流（今天 + 正文 + 九宫格媒体位 + 天气 chip + 编辑/删除图标）+ 右侧 dock 今日卡

## 交付对照

- **01 签名/封面字段**（d28ad38）：User.Signature（≤100 校验）/CoverUrl；PUT /me/profile + POST/DELETE /me/cover（复用头像压缩管线）；/auth/me、/auth/login、/friends、内容 author DTO 全量下发；e2e 5 条
- **02 朋友圈 UI**（b98847f）：ProfileHeader 公共组件（封面+压角头像+签名，moments/profile 复用）；moments 页日期分组流（今天/昨天/M月/yyyy年M月 左列日期）；profile 好友主页同布局；设置页「个人资料」区（签名编辑+封面上传/移除，与头像行整合）

## 已知限制

- 默认封面的渐变尾段硬编码暖色（#f5c98a/#f7dcb4）——用户上传封面后不可见，深色主题下无封面时会偏亮（后续可 token 化）
- 朋友圈流的作者行不显示头像（微信同款——个人页自己的内容不重复头像）
