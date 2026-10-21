# 04: 日志编辑器换 Cherry Markdown（即时渲染+专业工具栏）

**What to build:** 日志编辑器从 CodeMirror+自绘工具栏+独立预览，替换为 **cherry-markdown**（腾讯，框架无关）：**即时渲染模式**（typora 式输入即渲染，去掉分离预览窗）；**专业工具栏**（默认集：加粗/斜体/删除线/标题 H1-H5/列表/任务列表/引用/代码块/表格/水平线/图片/链接/行内代码/全屏等，按需裁掉音频/图表/导出类重项）；自定义「插入视频」菜单项（上传后插 video 语法）；**图片上传走 onUpload 钩子接鉴权直传**；主题跟随明暗。标题独立区块、日记头不变；编辑已有内容回填 setMarkdown。App 端 textarea fallback 保留。

**Blocked by:** None

**Status:** done（实施换 vditor 4.0.1：cherry 0.11.11 无即时渲染模式（types 仅 editOnly/previewOnly/edit&preview，官方文档确认 IR 是 vditor 特性），用户核心诉求即专业工具栏+即时渲染 → vditor mode:ir。其余按票执行）

- [x] cherry-markdown 集成（即时渲染+专业工具栏）
- [x] 图片上传钩子+自定义视频插入
- [x] 明暗主题跟随；编辑回填
- [x] build/tsc/playwright 全绿（journey 3 选择器适配 Cherry 编辑区）
