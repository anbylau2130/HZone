# 03: 日志编辑器升级为专业 markdown 编辑器（md-editor-v3）

**What to build:** 日志发布/编辑的编辑区替换为 md-editor-v3（Vue3 专业 markdown 编辑器）：内置完整工具栏（加粗/标题/列表/引用/代码/链接/表格等）、所见即所得或分屏预览、全屏；主题跟随 HZone 明暗模式；图片上传走 onUploadImg 钩子接 HZone 鉴权上传（/media，fetch 直传）返回 url 插入；视频插入用编辑器实例的 insert 能力保留现有 <video> 语法；标题独立区块不变。仅 H5 生效（#ifdef H5，App 端保留现有 textarea 方案）。编辑已有内容时编辑器回填现有 markdownBody。

**Blocked by:** None

**Status:** ready-for-agent

- [ ] md-editor-v3 集成（H5 分支），工具栏/预览/主题跟随可用
- [ ] 图片上传钩子接鉴权上传；视频插入保留
- [ ] 编辑回填 + 发布/保存链路回归
- [ ] build/tsc/playwright 全绿
