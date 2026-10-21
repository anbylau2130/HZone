# 05: 集成 drawIO 图表绘制

**What to build:** 日志编辑器集成 cherry 的 drawIO 画图（官方 demo 同款）：
- cherry 配置 `drawio`（查包内 README/types 拿准确键名与期望的 iframeSrc 形态——README 用 jgraph.github.io viewer，编辑器用 embed.diagrams.net 嵌入模式；两者均实测可达 200）
- 工具栏 drawio 按钮（demo 原生项，恢复注册）；点击 → 弹 drawio 绘制 iframe（模态，z-index 3000 同既有修复）→ 完成后按 cherry 的机制插入（确认走 fileUpload 管线落 /media 持久化，还是插入 dataURL/XML 语法——按包内源码实际行为接线；若产出图片走 fileUpload 则历史内容兼容）
- 弹层 z-index 3000、上传鉴权、明暗跟随全部沿用既有修复
- e2e：点击 drawio 按钮 → iframe 模态出现且加载成功（drawio 源 200）；真实绘制自动化成本高，插入手动验收说明

**Blocked by:** None

**Status:** ready-for-agent

- [ ] cherry drawio 配置接线（iframeSrc 可达源）
- [ ] 工具栏按钮恢复 + 点击弹 iframe 模态（z-index 正确）
- [ ] 插入产物持久化路径确认（fileUpload 或语法）并接通
- [ ] e2e：drawio 模态加载断言；build/tsc/playwright 全绿
