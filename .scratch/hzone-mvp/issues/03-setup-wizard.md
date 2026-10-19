# 03: 初始化向导（管理员创建 + 系统配置）

**What to build:** 实例首次访问：`GET /setup/status` / `POST /setup/admin` 创建首个管理员（仅未初始化时可用，初始化后 409）。系统配置表落地（SMTP、媒体上限、邀请开关——值先落库，管理端点在 11）。

**Blocked by:** 02

**Status:** ready-for-agent

- [x] `GET /setup/status` → `{ initialized: false }`
- [x] `POST /setup/admin` 创建首个管理员；二次调用 409
- [x] 系统配置表落地，可读可写
- [x] e2e：初始化后 status 为 true，二次创建 409
