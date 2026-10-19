# 09: PAT（个人访问令牌）与 API 认证

**What to build:** 设置页生成/吊销 PAT（明文仅展示一次，落库哈希）。PAT 调 REST API：发说说/日志、读自己与可见名单内内容、评论、搜索。可见性规则与界面完全一致（同一数据层入口）。

**Blocked by:** 06, 07

**Status:** ready-for-agent

- [ ] PAT 生成（明文一次性展示）与吊销
- [ ] PAT 调 API：发说说/日志、读内容、评论、搜索
- [ ] PAT 权限=本人身份，可见性校验与 session 用户同口径
- [ ] e2e：PAT 发内容→@好友→好友 PAT 读到；第三方 PAT 访问 404
