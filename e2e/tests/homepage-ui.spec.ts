import { test, expect, type Page } from '@playwright/test'
import {
  ensureInstanceInitialized,
  registerAndVerifyViaApi,
  makeFriends,
  type TestUser
} from './helpers'

// 首页 UI 旅程：模块渲染/空态隐藏/热力格点击跳转/足迹去重
// 天气种子通过 mock route 注入（发布带天气随记），会话用 storage 注入（token 来自注册助手）

async function loginViaStorage(page: Page, user: TestUser) {
  await page.goto('/#/pages/login/login')
  await page.evaluate(([token, userJson]) => {
    localStorage.setItem('hz_token', token)
    localStorage.setItem('hz_user', userJson)
  }, [user.token, JSON.stringify({ id: user.userId, email: user.email, nickname: user.nickname, isAdmin: false })])
  await page.reload()
}

test('home modules render: today card, stats, heatmap, footprints, timeline', async ({ page }) => {
  await ensureInstanceInitialized()
  const user = await registerAndVerifyViaApi('homeui')
  const friend = await registerAndVerifyViaApi('homeui-friend')
  await makeFriends(user, friend) // 好友统计卡 > 0

  await loginViaStorage(page, user)

  // 种子：发布两条带天气随记（同城——足迹去重断言用）+ 一条无天气日志
  const post = async (path: string, data: object) => {
    const res = await page.request.post(`http://localhost:5000${path}`, {
      data, headers: { Authorization: `Bearer ${user.token}` }
    })
    expect(res.ok()).toBeTruthy()
  }
  await post('/contents/says', { text: '足迹种子一', recipientIds: [], weather: { city: '杭州', condition: '晴', tempC: 12 } })
  await post('/contents/says', { text: '足迹种子二（同城）', recipientIds: [], weather: { city: '杭州', condition: '多云', tempC: 15 } })
  await post('/contents/journals', { title: '无天气日志', markdown: '正文', recipientIds: [] })

  await page.goto('/#/pages/home/home')
  await expect(page.getByText('连续记录', { exact: true })).toBeVisible()

  // 今日卡：公历+农历行
  await expect(page.locator('[class*="today"]').first()).toBeVisible()
  await expect(page.getByText(/月.*日/).first()).toBeVisible()

  // 统计行四卡标签
  for (const label of ['随记', '日志', '好友', '连续记录']) {
    await expect(page.getByText(label, { exact: true }).first()).toBeVisible()
  }

  // 热力格渲染且点格跳时间线
  const cell = page.locator('.heatmap .cell:not(.blank)').first()
  await expect(cell).toBeVisible()
  await cell.click()
  await expect(page).toHaveURL(/timeline/)

  // 足迹：同城连续去重 → 只有$rows 一条「杭州」
  await page.goto('/#/pages/home/home')
  await expect(page.locator('.footprints .city')).toHaveText(['杭州'])
})

test('home empty modules hidden: no weather content → footprints hidden', async ({ page }) => {
  await ensureInstanceInitialized()
  const user = await registerAndVerifyViaApi('homeui-empty')

  await loginViaStorage(page, user)

  await page.goto('/#/pages/home/home')
  await expect(page.locator('[class*="footprint"]')).toHaveCount(0)
  await expect(page.getByText(/从今天开始|连续/).first()).toBeVisible()
})
