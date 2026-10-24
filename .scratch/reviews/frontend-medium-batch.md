# 前端 Medium 批 7 项重审（2026-10-24）

背景：此前一轮审查提出 7 项 Medium，结论丢失。本次逐项重核现状（源码级证据），
**4 项已修复**（随本批提交），3 项确认现状并暂缓（理由见各项）。

## 已修复（本批）

### 1. formatRelativeTime i18n（原：硬编码中文）
- 现状判定：曾存在。`utils/format.ts` 的「刚刚 / n 分钟前 / n 小时前」硬编码中文
  （i18n 已 import 却未用），en-US 界面下相对时间仍显示中文
- 修复：新增 `common.justNow / minutesAgo / hoursAgo`（zh/en），三处分支改走
  `i18n.global.t`；跨年月日段本就是数字格式无需 i18n

### 2. FriendPicker 编辑回显（原：打开时勾选态清空）
- 现状判定：曾存在。`FriendPicker.vue` 打开时 `selectedIds = []` 无预填——
  编辑带收件人的内容打开选择器全未勾选，直接确认会**整体替换丢掉原收件人**
- 修复：新增 `selected?: FriendInfo[]` prop，打开时预填 id；两个调用方
  （publish.vue 传 `pickedFriends`、import-export.vue 传 `batchFriends`）接入

### 3. 分页错误态（原：fetcher 失败 rejection 逃逸）
- 现状判定：部分存在。`usePagination.load` 只有 try/finally 无 catch——
  请求层已 toast、loading 复位、loaded 保持 false 可重试（功能面 OK），
  但 fire-and-forget 调用方（home 等 refresh() 无 .catch）产生 unhandled rejection
- 修复：load 内 catch 吞掉（注释说明 toast 归请求层）。未做 error ref /
  重试 UI——当前所有列表页都是滚动重试模型，无静态重试按钮需求

### 4. 401 轮询（原：会话失效后定时器继续打 401）
- 现状判定：部分存在。请求层 401 清会话+跳登录，但 inbox 60s 定时器不停：
  token 清空后 refresh() 会早退（空转无害），但 reLaunch 若未触发 onHide
  则定时器泄漏，重新登录前每次 tick 都是无谓调用
- 修复：refresh catch 判 `status === 401` 即 stopPolling()

## 确认现状、暂缓（含理由）

### 5. blob 泄漏
- `api/media.ts` 模块级 Map 缓存 blob URL，永不 revoke/逐出。增长边界 =
  会话内看过的不同媒体数（长刷信息流内存缓慢增长）；`downloadBlob` 的临时
  URL 已正确 revoke（client.ts）。`client.ts` 的 401 重定向不涉及 blob
- 暂缓理由：正确修法是 LRU 上限+逐出 revoke，但被显示中的图逐出后 re-render
  会裂图，需要引用计数配合，改动面大；当前泄漏有界（会话级），优先级低

### 6. file picker 收敛
- H5 原生 `input[type=file]` 模式散落：publish.vue（图片+录屏入口）、
  settings.vue（头像）、moments.vue（封面）、CherryEditor 自研媒体菜单
  （insertImage/Audio/Video/File 四个 customMenu）——约 6 处，各自 accept/
  多选/video 逻辑有差异
- 暂缓理由：纯代码质量项无行为 bug；收敛成一个 `pickH5Files(options)` 工具
  需逐处核对 accept/count/video 差异，性价比一般，等下次动这些文件时顺手做

### 7. safeStorage
- 裸 localStorage 点：publish.vue previewOn watch 的 setItem（无 try/catch）、
  theme/locale store 若干处；client.ts 的会话读写走 uni.*StorageSync（uni 有
  内部降级）。getStoredUser / saveDraft 已有 try/catch
- 暂缓理由：现代浏览器隐私模式 localStorage 可写不抛、配额异常场景极边缘；
  已有的关键路径（草稿、会话读）都有防护，剩余裸点风险极低

## 关联记录

- 头像 /media 401：已核实修复（AvatarCircle/ProfileHeader 全站 blob 换取）
- cherry 预览图片首渲染 401：已知问题（见 .scratch/security/cherry-preview-xss-eval.md 遗留节）

## 重审过程中的新发现

### 时间戳序列化丢 Z 后缀 → 全站时间显示偏移时区差（✅ 已修复，见后续提交）
- 现象：刚创建的内容显示「8 小时前」（UTC+8 环境实测）
- 根因：列表/详情接口的 createdAt 序列化为 `"2026-10-24T05:32:11.705598"`
  （无 Z）。EF Core SQLite 读回 DateTime 的 Kind=Unspecified，System.Text.Json
  对 Unspecified 不写 Z；而创建接口返回内存实体（Kind=Utc）带 Z——同一字段
  两条路径不一致。前端 `new Date(无Z串)` 按**本地时间**解析 → UTC+8 用户看
  到的一切时间比实际早 8 小时（相对时间膨胀 8h；formatDateTime 显示的是
  UTC 钟面当本地钟面）
- 建议修法：API JsonOptions 加自定义 DateTimeConverter（Unspecified →
  SpecifyKind(Utc) 后写 ISO 带 Z），一处修复全站响应；注意跑全量后端测试
  （87 条）确认无格式断言破坏，并知会用户：修后所有历史时间会变为真实
  本地时间（比之前显示晚 8 小时，因为之前是错的）
- **已实施**：HZone.Api/UtcDateTimeJsonConverter.cs + Program.cs
  ConfigureHttpJsonOptions 接线。后端 116/116、e2e 23/23 通过；浏览器实测
  刚建日志显示「1 分钟前」（修复前同场景「9 小时前」）、随记分组「今天」
  正确、PAT 绝对时间显示真实北京时间（08:23 = 00:23Z）
- 顺带：groupByCnDate 的中国区日界恰好因该 bug 与 +8 偏移相互抵消，
  修复后日界计算仍正确（实测分组「今天」断言通过）
