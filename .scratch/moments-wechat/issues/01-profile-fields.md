# 01: 签名与封面字段（后端）

**What to build:** User 加 Signature（≤100 字）与 CoverUrl 两字段（EF migration：`dotnet ef migrations add AddSignatureCover -p src/HZone.Infrastructure -s src/HZone.Api`）；`PUT /me/profile` { signature } 与 `POST /me/cover`（multipart 图片，复用头像管线）；/auth/me、/auth/login、/friends、profile 内容 DTO 下发 signature/coverUrl。e2e：改签名/传封面→各 DTO 生效；签名超长 400；非图片 400。

**Blocked by:** None

**Status:** ready-for-agent

- [ ] 字段+迁移+端点（profile/cover）
- [ ] DTO 下发（me/login/friends/profile 场景）
- [ ] e2e 全绿（≥109）
