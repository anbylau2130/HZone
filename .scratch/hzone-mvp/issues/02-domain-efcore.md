# 02: 领域模型与 EF Core（SQLite）+ 可见名单单一查询入口

**What to build:** PRD 全部实体的 EF Core 模型与 SQLite 落地：用户、双向好友、内容（说说/日志）、可见名单（@）、评论、站内信、邀请码、媒体资产、PAT、系统配置。可见名单校验收敛到数据层单一查询入口；硬删除级联清理评论/站内信/媒体文件。一条 e2e 测试验证：作者+@者可见、他人 404。

**Blocked by:** 01

**Status:** ready-for-agent

- [x] 全部实体 + SQLite EF Core 配置（关系/索引），EnsureCreated 或迁移可用
- [x] 可见名单单一查询入口存在且被内容读取使用
- # **Acceptance（e2e）**
- [x] 作者发布不@内容 → 仅作者可见
- [x] @ 好友 → 该好友可读，非好友 404
