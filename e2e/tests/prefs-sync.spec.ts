import { test, expect } from '@playwright/test'
import {
  ensureInstanceInitialized,
  registerAndVerifyViaApi,
  loginViaUi,
  fillUniInput,
  uniButton,
  API_BASE
} from './helpers'

// 账号级偏好同步旅程（PRD 6.9 欠账补齐）：
// A 端 UI 改主题深色 + 语言 English → API 断言 preferences 落库 →
// 新 context（= 新设备）登录同账号 → 深色 + 英文已同步。

// 轮询 /auth/me 直到 preferences 落库（防抖 1s + 网络延迟）
async function waitForPreferences(token: string): Promise<Record<string, unknown>> {
  const deadline = Date.now() + 10_000
  for (;;) {
    const res = await fetch(`${API_BASE}/auth/me`, {
      headers: { Authorization: `Bearer ${token}` }
    })
    const me = (await res.json()) as { preferences?: Record<string, unknown> }
    const prefs = me.preferences
    if (prefs?.mode === 'dark' && prefs?.locale === 'en-US') return prefs
    if (Date.now() > deadline) {
      throw new Error(`preferences not synced: ${JSON.stringify(prefs)}`)
    }
    await new Promise((r) => setTimeout(r, 300))
  }
}

test('preferences made on device A sync to device B after re-login', async ({ page, browser }) => {
  await ensureInstanceInitialized()
  const user = await registerAndVerifyViaApi('prefsui')

  // A 端：真实 UI 登录 → 主题页改深色 + English
  await loginViaUi(page, user)
  await page.goto('/#/pages/theme/theme')
  await expect(page.locator('.seg-item', { hasText: '深色' })).toBeVisible()
  await page.locator('.seg-item', { hasText: '深色' }).click()
  await page.locator('.seg-item', { hasText: 'English' }).click()

  // 防抖 1s 后整包 PUT：轮询 API 直到 preferences 落库
  const prefs = await waitForPreferences(user.token)
  expect(typeof prefs.presetId).toBe('string')

  // A 端本地已生效：深色 CSS 变量 + 英文存储
  const bg = await page.evaluate(() => document.documentElement.style.getPropertyValue('--hz-color-bg'))
  expect(bg.trim()).toBe('#1e1712')
  expect(await page.evaluate(() => localStorage.getItem('hz_locale'))).toBe('en-US')

  // B 端（新 context = 新设备）：登录同账号 → 深色 + 英文已同步（账号偏好优先于本地）
  const ctx2 = await browser.newContext()
  const page2 = await ctx2.newPage()
  await page2.goto('/#/pages/login/login')
  await fillUniInput(page2, '邮箱', user.email)
  await fillUniInput(page2, '密码', user.password)
  await uniButton(page2, '登录').click()
  await expect(page2.locator('.today-card')).toBeVisible({ timeout: 20_000 })

  const bg2 = await page2.evaluate(() => document.documentElement.style.getPropertyValue('--hz-color-bg'))
  expect(bg2.trim()).toBe('#1e1712')
  expect(await page2.evaluate(() => localStorage.getItem('hz_locale'))).toBe('en-US')

  // 可见证据：主题页模式区英文渲染（语言同步）
  await page2.goto('/#/pages/theme/theme')
  await expect(page2.locator('.seg-text', { hasText: 'Dark' })).toBeVisible({ timeout: 10_000 })
  await expect(page2.locator('.section-title', { hasText: 'Mode' })).toBeVisible({ timeout: 10_000 })
  await ctx2.close()
})



test('guest changes stay local-only (no crash, no sync)', async ({ page }) => {
  await ensureInstanceInitialized()
  // 未登录直接进主题页：改动只落本地，不 PUT（无 token 跳过）
  await page.goto('/#/pages/theme/theme')
  await expect(page.locator('.seg-item', { hasText: '深色' })).toBeVisible()
  await page.locator('.seg-item', { hasText: '深色' }).click()
  await expect.poll(async () =>
    page.evaluate(() => document.documentElement.style.getPropertyValue('--hz-color-bg'))
  ).toBe('#1e1712')
  // 未登录：无 hz_token → schedulePreferencesSync 直接跳过，无网络请求
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem('hz_theme') || '{}').mode)).toBe('dark')
})
