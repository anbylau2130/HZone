# HZone MVP 验收报告（ticket 15）

**日期:** 2026-10-20 ｜ **结论:** 全部通过

- 进程内 API e2e（WebApplicationFactory，dotnet test）：**87/87 绿**（tests/HZone.Tests/）
- Playwright UI e2e（chromium，本地 dotnet run :5000 + H5 dev :5173，无 Docker）：**6/6 绿**（e2e/tests/hzone-journey.spec.ts）
- 一键验收：./run-mvp-tests.ps1（dotnet test → e2e → npx playwright test）

## 28 条用户故事 × 覆盖证据

测试类均在 tests/HZone.Tests/，UI 旅程均在 e2e/tests/hzone-journey.spec.ts（下称"UI 旅程 n"）。

### 账号与注册

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 1 | 管理员初始化向导 | SetupWizardTests.Wizard_flow_initializes_instance_and_unblocks_contents；SMTP 配置项见 #28 | ✅ |
| 2 | 邀请码+邮箱+密码注册 + 邮件验证 | API：AuthFlowTests.Full_flow_register_verify_login_me_forgot_reset、UserStoryCoverageTests.Invite_journey_register_verify_friend_mention_visible（全 HTTP 旅程，.eml 提取 token）；UI：旅程 1（注册页填邀请码 → 邮件链接落 /#/pages/verify-email/verify-email → 验证成功 → 登录） | ✅ |
| 3 | 邀请码一次性/有效期/配额 | AuthFlowTests.Register_with_wrong_invite_code_returns_400、Register_with_expired_invite_code_returns_400、Fourth_active_invite_returns_409 | ✅ |
| 4 | 管理员全局关闭邀请 | AdminConfigTests.Partial_update_flow（invites-disabled → 创建邀请 403） | ✅ |
| 5 | 邮箱重置密码 | Full_flow…（恒 200 + .eml token + 新密码登录） | ✅ |
| 6 | 邮箱即账号登录 | Full_flow…（混合大小写注册 → 小写归一登录）、UserStoryCoverageTests.Invite_journey…；UI 旅程 1 | ✅ |
| 7 | 好友申请→同意→双向好友 | FriendTests.Request_accept_then_both_see_each_other_outsider_does_not；UI 旅程 2 起点为双向好友 | ✅ |
| 8 | 查看/移除好友 | FriendTests.Delete_friend_bilateral_unfriend | ✅ |

### 内容与可见性

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 9 | 说说（文本+媒体）+ @ 双向好友 | SayMediaTests.Publish_say_with_two_images_friend_downloads_media_outsider_404；UI 旅程 2 | ✅ |
| 10 | 日志（标题+Markdown）+ @ | VisibilityTests.Journal_visible_to_mentioned_friend_with_full_body；UI 旅程 3 | ✅ |
| 11 | 不 @ 任何人 = 私密 | VisibilityTests.Private_say_visible_only_to_author | ✅ |
| 12 | 被 @ 者看"收到的内容" | JournalSearchTests.Received_lists_mentions_from_others_excluding_own | ✅ |
| 13 | 非名单成员 404 而非 403 | VisibilityTests.Mentioned_friend_can_read_non_friend_gets_404（陌生人/匿名）；UserStoryCoverageTests.Friend_but_not_mentioned_gets_404（好友但未@ → 404，详情与主页列表都不泄露存在性） | ✅ |
| 14 | 修改可见名单（新增收站内信、移除立即失权） | UserStoryCoverageTests.Edit_recipients_new_gains_access_removed_loses_immediately（PUT recipientIds 整体替换；被移除者详情 404 + 收到列表消失；新增者可见 + Mentioned 站内信）；Edit_body_keeps_remaining_recipient_access_with_new_text（编辑后名单内好友仍可见最新内容） | ✅ |
| 15 | 编辑正文/媒体、硬删除级联 | SayMediaTests.Edit_say_text_via_put_author_only、Delete_content_cascades_db_rows_and_disk_files、CommentsInboxTests.Content_delete_cascades_inbox_messages | ✅ |
| 16 | 好友主页（Ta 发的、@了我的） | API：JournalSearchTests.Home_timeline_mentioned_friend_sees_journal_outsider_gets_empty；UI：旅程 3（好友页 → 主页 → 日志标题可见） | ✅ |
| 17 | 关键词搜索自己的内容 | JournalSearchTests.Search_hits_title_body_and_say_text_own_only；UI 旅程 4 | ✅ |

### 互动

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 18 | 名单成员评论 | CommentsInboxTests.Friend_comment_notifies_author_and_appears_in_list；UI 旅程 2（详情页发评论） | ✅ |
| 19 | 登录后站内信看 @ 与评论事件 | CommentsInboxTests.Publish_with_recipient_writes_mentioned_inbox、Mark_read_is_idempotent_and_nonowner_gets_404；UI 旅程 2（红点数 → 站内信列表 → 打开进详情 → 对方刷新见红点） | ✅ |

### 数据与迁移

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 20 | 整站导出 zip（md+媒体+清单） | ExportImportTests.Export_zip_contains_journals_says_media_manifest、Export_excludes_other_users_content | ✅ |
| 21 | zip 批量导入（默认私密） | ExportImportTests.Import_into_clean_instance_restores_content_and_media_privately | ✅ |
| 22 | 导入内容批量编辑 @ | ExportImportTests.Batch_add_recipients_makes_friend_visible_and_idempotent、Batch_endpoint_skips_non_author_content_and_reports_count | ✅ |
| 23 | PAT 生成/吊销 | PatTests.Generate_pat_returns_plaintext_once_and_authenticates_owner、Revoked_pat_gets_401_and_revoke_is_idempotent、Pat_limit_20_then_409_revoke_frees_slot | ✅ |
| 24 | agent 用 PAT 读写 | PatTests.Pat_posts_say_mention_friend_both_readers、Pat_reads_own_contents_received_and_search、Outsider_pat_cannot_read_non_visible_content（PAT 与 JWT 同可见性口径） | ✅ |

### 界面与个性化

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 25 | 中/英文切换 | UI 旅程 5：settings 语言段切换 zh→en，断言 Language 可见，刷新保持 | ✅ |
| 26 | 自定义主题（design token） | UI 旅程 5：浅→深模式切换 + CSS 变量断言 + 刷新保持；settings 自定义配色 4 token + 对比度校验（H5）已实现 | ✅（注：主题存浏览器 localStorage，账号级"跨端同步"未实现，如实标注） |

### 管理员

| # | 用户故事 | 证据 | 状态 |
|---|---------|------|------|
| 27 | 媒体上限与全局开关 | AdminConfigTests.Partial_update_flow、AdminMediaLimitTests（压缩降级拒绝、413） | ✅ |
| 28 | 配置 SMTP | AdminConfigTests.Partial_update_flow（smtp host/port/hasPassword）+ 注册/重置邮件走 FileEmailSender（本地 outbox .eml） | ✅ |

## 本票交付内容

### API e2e 补全（UserStoryCoverageTests.cs，4 项新测试）

- story 13 负路径：好友但未 @ → 404（详情与好友主页列表都不泄露存在性）
- story 14：可见名单编辑（新增者收 Mentioned 站内信 + 被移除者立即失权）——**这是本次验收发现的实现缺口，已跨栈补齐**：ContentEndpoints.cs PUT /contents/{id} 支持 recipientIds 整体替换（新增者写 Mentioned 站内信、移除删行即失权），ContentContracts.cs 加 RecipientIds
- story 14/15：编辑正文后名单内好友仍可见最新内容
- stories 2/3/7/12：邀请码注册→验证→登录→加好友→@→可见 全 HTTP 旅程（.eml 提取 token）

### UI e2e（e2e/tests/{helpers,hzone-journey}.ts）

- 旅程 1 注册→验证→登录；旅程 2 发说说@好友→红点→详情→评论→对方刷新见红点；旅程 3 日志→好友主页可见；旅程 4 搜索命中；旅程 5 主题/语言切换跨刷新保持；旅程 6 游客访问受保护页 → 跳登录
- helpers：registerAndVerifyViaApi（HTTP 注册 + 读 API 进程 outbox .eml 提取 token 验证）、makeFriends、loginViaUi、uni-app H5 自定义元素选择器（按占位符文本定位 uni-input）

### 验收中发现并修复的旅程阻断 bug（跨栈）

1. frontend/src/pages/friends/friends.vue：模板引用未定义的 incoming/outgoing（应为 requests.incoming/outgoing）→ 好友页渲染崩溃。已修。
2. frontend/src/locale/zh-CN.json inbox.mentioned：vue-i18n 把 @ 当 linked-message 语法 → 消息编译失败渲染异常。改为 {'@'} 转义。已修。
3. src/HZone.Api/Endpoints/AuthEndpoints.cs：验证/重置邮件链接路由少 pages/… 前缀（/#/verify-email → /#/pages/verify-email/verify-email）→ 点邮件链接落地空白页。已修。
4. src/HZone.Api/Properties/launchSettings.json：API 端口 5097 → 5000，与 vite 代理/playwright 配置对齐（否则 UI 套件连不上 API）。已修。

## 已知限制（如实标注）

- story 26"主题跨端同步"：主题（模式/预置/自定义色）存浏览器 localStorage，未实现账号级跨端同步。设置页 4-token 自定义 + 对比度校验 + 深浅模式均有 UI 测试证据。
- UI e2e 按用户要求只覆盖 H5（spec：MVP 测试只针对 H5）。
