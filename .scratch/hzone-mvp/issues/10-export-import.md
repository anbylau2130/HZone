# 10: 整站导出/导入（zip）

**What to build:** 整站导出 zip（日志 md + 说说文本与媒体 + 元数据清单），导出即备份；zip 批量导入 md 日志（默认私密，仅自己可见）；导入后支持多选内容批量编辑 @ 名单（批量追加可见名单）。

**Blocked by:** 07

**Status:** ready-for-agent

- [ ] 整站导出 zip：日志为 .md（frontmatter 含元数据）+ 说说媒体 + manifest
- [ ] 导出的 zip 可回导
- [ ] zip 导入 md 日志：默认私密
- [ ] 批量编辑 @ 名单端点（多选内容→批量追加）
- [ ] e2e：导出→清库→导入→内容恢复（默认私密）
