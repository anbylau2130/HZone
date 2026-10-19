import { expect, test, type Browser } from '@playwright/test'
import {
  createInviteCode,
  ensureInstanceInitialized,
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
const SAY_TEXT = 'UI 旅程：今天天气很好，适合发说说'
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
})

// ---------- 旅程 2：A 发说说 @ B → B 站内信红点 → 详情 → 评论 → A 看到评论站内信 ----------

test('journey 2: say with mention, inbox red dot, detail comment loop', async ({ browser }) => {
  const pageA = await newSession(browser, alice)
  await openEntry(pageA, '发布内容')
  await pageA.locator('uni-textarea textarea').fill(SAY_TEXT)
  await uniButton(pageA, '选择好友').click()
  await pageA.locator('.sheet .row', { hasText: bob.nickname }).click()
  await uniButton(pageA, '确定').click()
  await uniButton(pageA, '发布').click()
  await pageA.waitForResponse((r) => r.url().includes('/api/contents/says') && r.status() === 201)
  await expect(pageA.getByText('发布成功')).toBeVisible()

  // B 登录：首页站内信红点（friendRequest + mentioned = 2 条未读）
  const pageB = await newSession(browser, bob)
  const inboxEntry = pageB.locator('.entry', { hasText: '站内信' })
  await expect(inboxEntry.locator('.dot')).toBeVisible({ timeout: 10_000 })
  await expect(inboxEntry.locator('.dot')).toHaveText('2')
  await inboxEntry.click()

  // 站内信列表：mentioned 事件 → 进详情 → 评论
  const item = pageB.locator('.item', { hasText: '内容中 @ 了你' })
  await expect(item.first()).toBeVisible()
  await item.first().click()
  await expect(pageB.getByText(SAY_TEXT)).toBeVisible()
  await fillUniInput(pageB, '写评论…', 'UI 旅程评论：同意！')
  await uniButton(pageB, '发送').click()
  await expect(pageB.getByText('UI 旅程评论：同意！')).toBeVisible()

  // A 的会话回到前台（刷新触发站内信拉取，对齐"登录后可见"的产品语义）
  await pageA.reload()
  await expect(pageA.getByText(/你好，/)).toBeVisible({ timeout: 15_000 })
  const aliceInboxEntry = pageA.locator('.entry', { hasText: '站内信' })
  await expect(aliceInboxEntry.locator('.dot')).toBeVisible({ timeout: 10_000 })
  await aliceInboxEntry.click()
  await expect(pageA.getByText('评论了你的内容').first()).toBeVisible()
})

// ---------- 旅程 3：日志发布 → B 好友主页可见 ----------

test('journey 3: journal publish visible on friend profile', async ({ browser }) => {
  const pageA = await newSession(browser, alice)
  await openEntry(pageA, '发布内容')
  await uniButton(pageA, '日志').click()
  await fillUniInput(pageA, '标题', JOURNAL_TITLE)
  await pageA.locator('uni-textarea textarea').fill('正文第一段，写给 Bob 看。')
  await uniButton(pageA, '选择好友').click()
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
  await fillUniInput(page, '搜索我的内容', 'UI 旅程')
  await uniButton(page, '搜索').click()
  await expect(page.getByText(SAY_TEXT).first()).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(JOURNAL_TITLE).first()).toBeVisible()
})

// ---------- 旅程 5：主题/语言切换并跨刷新保持 ----------

test('journey 5: theme dark persists, language en persists after reload', async ({ browser }) => {
  const page = await newSession(browser, alice)
  await page.locator('.settings-entry').click()
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
  await page.goto('/#/pages/index/index')
  await expect(uniButton(page, '登录')).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/pages\/login\/login/)
})
