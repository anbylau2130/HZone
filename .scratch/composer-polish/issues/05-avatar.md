# 05: 头像配置（后端+设置页+全站显示）

**What to build:** User 加 AvatarUrl；POST/DELETE /me/avatar（复用媒体压缩管线，仅图片）；/auth/me、/auth/login、/friends、内容 author DTO 下发 avatarUrl；设置页顶部 72px 头像行（上传/移除）；AvatarCircle 组件全站替换（侧栏/ContentCard/moments），无头像回落昵称首字。dev 库在线 ALTER TABLE 补列。

**Blocked by:** 03（侧栏冲突规避）

**Status:** done

- [x] 后端 avatar 字段+上传/清除端点+e2e（AvatarTests 3 条）
- [x] 设置页顶部头像行
- [x] 侧栏/随记卡/朋友圈显示头像
- [x] dotnet 106/106 + playwright 12/12 全绿（ef11be9，独立复核）
