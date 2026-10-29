import { expect, test, type Browser } from '@playwright/test'
import {
  createInviteCode,
  ensureInstanceInitialized,
  enterJournalEditMode,
  loginViaUi,
  openEntry,
  registerAndVerifyViaApi,
  uniButton,
  fillUniInput,
  extractVerifyToken,
  uniqueEmail,
  type TestUser,
} from './helpers'

test.describe.configure({ mode: 'serial' })

const PASSWORD = 'UiPassw0rd!x'

// 共享旅程用户（beforeAll 经 HTTP 注册并互为好友）
let alice: TestUser
let bob: TestUser
const SAY_TEXT = 'UI 旅程：今天天气很好，适合发随记'
const JOURNAL_TITLE = 'UI 旅程日志标题'

test.beforeAll(async () => {
  await ensureInstanceInitialized()
  alice = await registerAndVerifyViaApi('AliceUi')
  bob = await registerAndVerifyViaApi('BobUi')
  await makeFriendsWrapped()
})

async function makeFriendsWrapped() {
  const { makeFriends } = await import('./helpers')
  await makeFriends(alice, bob)
}

// 每个用户独立 browser context（独立 localStorage 会话），免去登出/返回导航。
async function newSession(browser: Browser, user: TestUser): Promise<Page> {
  const ctx = await browser.newContext()
  const page = await ctx.newPage()
  await loginViaUi(page, user)
  return page
}

// ---------- 旅程 1：注册 → 邮箱验证 → 登录（全 UI） ----------

test('journey 1: register via UI, verify via email link route, login via UI', async ({ page }) => {
  const email = uniqueEmail('carlui')
  await page.goto('/#/pages/register/register')
  const code = await createInviteCode()
  await fillUniInput(page, '邀请码', code)
  await fillUniInput(page, '邮箱', email)
  await fillUniInput(page, '密码', PASSWORD)
  await fillUniInput(page, '昵称', 'CarlUi')
  await uniButton(page, '注册').click()
  await page.waitForResponse((r) => r.url().includes('/api/auth/register') && r.status() === 201)

  // 验证邮件链接落在本 H5 验证页（真实用户点击邮件链接的路径）
  const verifyToken = await extractVerifyToken(email)
  await page.goto(`/#/pages/verify-email/verify-email?token=${verifyToken}`)
  await expect(page.getByText('邮箱验证成功')).toBeVisible({ timeout: 15_000 })

  await loginViaUi(page, { email, password: PASSWORD, nickname: 'CarlUi', userId: '', token: '' })
  // ticket 04：登录后落在新首页，今日卡 + 四张统计卡
  await expect(page.locator('.today-card')).toBeVisible()
  await expect(page.locator('.stat-card')).toHaveCount(4)
})

// ---------- 旅程 2：A 发随记 @ B → B 站内信红点 → 列表页 → 内联评论 → A 看到评论站内信 ----------

test('journey 2: say with mention, inbox red dot, inline comment loop', async ({ browser }) => {
  const pageA = await newSession(browser, alice)
  await openEntry(pageA, '发布内容')
  await pageA.locator('uni-textarea textarea').fill(SAY_TEXT)
  // 随记模式：工具栏「选择好友」按钮（日志模式入口不同，见 journey 3 注释）
  await uniButton(pageA, '选择好友').click()
  await pageA.locator('.sheet .row', { hasText: bob.nickname }).click()
  await uniButton(pageA, '确定').click()
  await uniButton(pageA, '发布').click()
  await pageA.waitForResponse((r) => r.url().includes('/api/contents/says') && r.status() === 201)
  await expect(pageA.getByText('发布成功')).toBeVisible()

  // B 登录：首页侧栏「收到」tab 红点（friendRequest + mentioned = 2 条未读）
  const pageB = await newSession(browser, bob)
  const receivedTab = pageB.locator('.sb-item', { hasText: '收到' })
  await expect(receivedTab.locator('.dot')).toBeVisible({ timeout: 10_000 })
  await expect(receivedTab.locator('.dot')).toHaveText('2')
  await openEntry(pageB, '站内信')

  // 站内信列表：mentioned 事件 → 跳「收到」页并定位（被@ 的是别人的内容，
  // 随记页只显自己的；?focus 滚到该卡高亮）
  const item = pageB.locator('.item', { hasText: '内容中 @ 了你' })
  await expect(item.first()).toBeVisible()
  await item.first().click()
  await expect(pageB).toHaveURL(/pages\/received\/received/)

  // 通知已直达收到页并在原随记卡片上展开内联评论区回复（①A）
  const card = pageB.locator('.card', { hasText: SAY_TEXT }).first()
  await expect(card).toBeVisible({ timeout: 15_000 })
  await card.locator('.foot-item').first().click() // 「评论 n」展开评论区
  await fillUniInput(pageB, '写评论…', 'UI 旅程评论：同意！')
  await uniButton(pageB, '发送').click()
  await expect(pageB.getByText('UI 旅程评论：同意！')).toBeVisible()

  // A 的会话回到前台（回到首页触发站内信拉取，对齐"登录后可见"的产品语义）
  await pageA.goto('/#/pages/home/home')
  await expect(pageA.locator('.today-card')).toBeVisible({ timeout: 15_000 })
  const aliceReceivedTab = pageA.locator('.sb-item', { hasText: '收到' })
  await expect(aliceReceivedTab.locator('.dot')).toBeVisible({ timeout: 10_000 })
  await openEntry(pageA, '站内信')
  await expect(pageA.getByText('评论了你的内容').first()).toBeVisible()
})

// ---------- 旅程 3：日志发布 → B 好友主页可见 ----------

test('journey 3: journal publish visible on friend profile', async ({ browser }) => {
  const pageA = await newSession(browser, alice)
  // 去切换器后 kind 由入口 query 决定：直达 journal 入口
  await pageA.goto('/#/pages/publish/publish?kind=journal')
  await fillUniInput(pageA, '标题', JOURNAL_TITLE)
  // cherry-markdown 编辑器（H5，CodeMirror6 contenteditable）；新规则默认预览模式，先切「编辑」
  await enterJournalEditMode(pageA)
  const cm = pageA.locator('.cherry-editor .cm-content')
  await cm.click()
  await pageA.keyboard.type('正文第一段，写给 Bob 看。')
  // 日志模式收件人入口是「@ 谁」label 链接（选择好友按钮已移除，仅随记工具栏保留）
  await pageA.locator('.recipients .label').click()
  await pageA.locator('.sheet .row', { hasText: bob.nickname }).click()
  await uniButton(pageA, '确定').click()
  await uniButton(pageA, '发布').click()
  await pageA.waitForResponse((r) => r.url().includes('/api/contents/journals') && r.status() === 201)

  // B 登录 → 好友页 → Alice 主页 → 日志标题可见
  const pageB = await newSession(browser, bob)
  await openEntry(pageB, '好友')
  await pageB.locator('.row', { hasText: alice.nickname }).locator(uniButton(pageB, '主页')).click()
  await expect(pageB.getByText(JOURNAL_TITLE).first()).toBeVisible({ timeout: 15_000 })
})

// ---------- 旅程 4：搜索命中自己的内容 ----------

test('journey 4: search hits own content', async ({ browser }) => {
  const page = await newSession(browser, alice)
  await openEntry(page, '搜索')
  // 全局搜索页：跨随记/笔记/评论；命中以片段行呈现（自己的内容排在前）
  await fillUniInput(page, '搜随记、笔记、评论', 'UI 旅程')
  await page.locator('.gs-btn').click()
  await expect(page.locator('.gs-hit', { hasText: SAY_TEXT }).first()).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.gs-hit', { hasText: JOURNAL_TITLE }).first()).toBeVisible()
})

// ---------- 旅程 5：主题/语言切换并跨刷新保持 ----------

test('journey 5: theme dark persists, language en persists after reload', async ({ browser }) => {
  const page = await newSession(browser, alice)
  // tab 合并：主题设置并入设置页（独立主题 tab 已移除，桌面走 AppSidebar）
  await page.locator('.sb-item', { hasText: '设置' }).click()
  await expect(page.getByText('模式')).toBeVisible()

  // 浅 → 深
  const bgBefore = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--hz-color-bg').trim()
  )
  await page.locator('.seg-item', { hasText: '深色' }).click()
  const bgDark = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--hz-color-bg').trim()
  )
  expect(bgDark).not.toBe(bgBefore)
  await page.reload()
  const bgAfterReload = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--hz-color-bg').trim()
  )
  expect(bgAfterReload).toBe(bgDark)

  // 中文 → English，刷新保持
  await page.locator('.seg-item', { hasText: 'English' }).click()
  await expect(page.getByText('Language')).toBeVisible()
  await page.reload()
  await expect(page.getByText('Language')).toBeVisible({ timeout: 15_000 })
})

// ---------- 旅程 6：游客访问受保护页面 → 跳登录 ----------

test('journey 6: guest visiting protected page redirects to login', async ({ page }) => {
  await page.goto('/#/pages/home/home')
  await expect(uniButton(page, '登录')).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/pages\/login\/login/)
})

// ---------- 旅程 7：天气采集（mock 外部服务）→ 发布快照 → 阅读视图全显 ----------

test('journey 7: publish with mocked weather snapshot, reader shows lunar + weather', async ({
  browser
}) => {
  // ticket 04：新首页会自动采集天气（getWeather 模块级缓存），mock 必须在登录前装好，
  // 否则首页先用真实网络拿到城市，缓存与 mock 不一致。
  const ctx = await browser.newContext()
  const pageA = await ctx.newPage()
  // mock 浏览器直调的 ipwho.is 与 Open-Meteo（免真实外网依赖）
  await pageA.route('**ipwho.is**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ city: '杭州', latitude: 30.27, longitude: 120.15 })
    })
  )
  await pageA.route('**api.open-meteo.com**', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ current: { temperature_2m: 12, weather_code: 0 } })
    })
  )

  await loginViaUi(pageA, alice)

  // 直接进日志发布页（全新页面加载，?kind=journal 生效）；日记头天气条自动预览：
  // 城市 + 精确温度（含单位），mock 已就位
  await pageA.goto('/#/pages/publish/publish?kind=journal')
  await expect(pageA.getByText('杭州').first()).toBeVisible({ timeout: 15_000 })
  await expect(pageA.getByText('12°C').first()).toBeVisible()

  // 详情页已移除：改发日志，在日志阅读视图 meta 行断言农历+天气全显（与随记共用天气快照）
  await fillUniInput(pageA, '标题', '天气快照日志')
  // 新规则默认预览模式，先切「编辑」
  await enterJournalEditMode(pageA)
  const cm = pageA.locator('.cherry-editor .cm-content')
  await cm.click()
  await pageA.keyboard.type('UI 旅程：天气快照日志正文')
  // 日志模式收件人入口同上（label 链接）
  await pageA.locator('.recipients .label').click()
  await pageA.locator('.sheet .row', { hasText: bob.nickname }).click()
  await uniButton(pageA, '确定').click()
  const respPromise = pageA.waitForResponse(
    (r) => r.url().includes('/api/contents/journals') && r.status() === 201
  )
  await uniButton(pageA, '发布').click()
  await respPromise

  // 日志阅读视图（默认视图）：最新一篇在最上，meta 行全显农历+天气
  // （农历宽松匹配：含"年"且含"月"；天气含城市+温度）
  await pageA.goto('/#/pages/journals/journals')
  const meta = pageA.locator('.article', { hasText: '天气快照日志' }).locator('.r-meta')
  await expect(meta).toBeVisible({ timeout: 15_000 })
  const text = (await meta.textContent()) ?? ''
  expect(text).toContain('年')
  expect(text).toContain('月')
  expect(text).toContain('杭州')
  expect(text).toContain('12°C')
})
