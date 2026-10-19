# 07: 日志、搜索

**What to build:** 日志=标题+Markdown 正文+@字段；编辑/删除同说说。基础搜索：LIKE 搜索自己的日志标题/正文与说说文本，接口抽象好（未来换全文索引不动 API）。

**Blocked by:** 05

**Status:** ready-for-agent

- [x] 日志 CRUD，可见名单规则同 02/06
- [x] `GET /search?q=` 只搜自己的内容，LIKE 起步
- [x] e2e：发布日志→@好友→好友可见；搜索命中/不命中
