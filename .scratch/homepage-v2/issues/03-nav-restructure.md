# 03: 导航重组：六 tab + 设置/主题拆分 + 搜索上移 + 布局收尾

**What to build:** 底部导航与桌面侧边栏改为六项：首页/随记/日志/收到/主题/设置。主题页=现 settings 页（模式/预置主题/自定义配色/语言）。设置页=旧首页 hub 的入口（好友/PAT/导入导出/管理后台/退出；搜索不放这里——上移为随记/日志页标题栏搜索图标，跳 search 页）。首页 tab 本票暂指旧 index 页（新首页在 04 落地）。桌面 AppSidebar 同步六项。**同时收尾 t19 中断遗留**：随记/日志页双按钮去重（列表为空时隐藏顶栏按钮）、内容列统一 42rem 居中、空态 60vh 垂直居中——**先审工作区已有未提交 WIP 再继续**。

**Blocked by:** None (can start immediately)

**Status:** ready-for-agent

- [ ] 六 tab（AppTabBar+AppSidebar 同步项与图标），首页 tab 暂指旧 index
- [ ] 主题页=现 settings；设置页=hub 入口迁移
- [ ] 搜索上移：随记/日志标题栏搜索图标
- [ ] 布局收尾三处（先审 WIP）
- [ ] build/tsc/playwright 6/6 全绿
