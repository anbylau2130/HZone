// 日志编辑复用创建页：detail 编辑图标 → publish?id=（cherry 全套编辑器回填）→ 保存 PUT →
// 返回 detail 自动刷新。替代旧的 detail 内联 textarea（编辑/创建两个页面的割裂）。
import { expect, test } from '@playwright/test'
import { API_BASE, loginViaUi, registerAndVerifyViaApi, uniButton } from './helpers'

test('edit journal reuses publish page with cherry editor', async ({ page }) => {
  const user = await registerAndVerifyViaApi('EditUser')

  // API 直接造一篇日志作为编辑对象（recipientIds 空 = 仅自己可见）
  const create = await fetch(`${API_BASE}/contents/journals`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${user.token}`
    },
    body: JSON.stringify({
      title: '编辑前标题',
      markdown: '# 编辑前正文\n\n初始段落。',
      recipientIds: []
    })
  })
  expect([200, 201]).toContain(create.status)
  const journal = (await create.json()) as { id: string }

  await loginViaUi(page, user)
  await page.goto(`/#/pages/detail/detail?id=${journal.id}`)
  await expect(page.locator('.title')).toHaveText('编辑前标题')

  // 编辑图标：日志应跳 publish 编辑页（不再是 detail 内联 textarea）
  await page.locator('.head .acts .icon-btn').first().click()
  await expect(page.locator('.cherry-editor .cm-content')).toBeVisible({ timeout: 20_000 })
  await expect(page).toHaveURL(/pages\/publish\/publish/)

  // 回填：标题与正文来自原日志
  await expect(page.locator('.j-title input')).toHaveValue('编辑前标题')
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('初始段落')

  // 修改标题 + 正文文末追加（CM6：点击聚焦 → Ctrl+End → 键入）
  await page.locator('.j-title input').fill('编辑后标题')
  const cm = page.locator('.cherry-editor .cm-content')
  await cm.click()
  await page.keyboard.press('Control+End')
  await page.keyboard.type('\n\n编辑追加段落。')

  // 保存 → PUT → navigateBack 返回 detail，onShow 刷新出新标题
  await uniButton(page, '保存').click()
  await expect(page).toHaveURL(/pages\/detail\/detail/, { timeout: 15_000 })
  await expect(page.locator('.title')).toHaveText('编辑后标题', { timeout: 15_000 })

  // API 侧复核：标题与正文确实落库
  const after = await fetch(`${API_BASE}/contents/${journal.id}`, {
    headers: { Authorization: `Bearer ${user.token}` }
  })
  const body = (await after.json()) as { title: string; markdownBody: string }
  expect(body.title).toBe('编辑后标题')
  expect(body.markdownBody).toContain('编辑追加段落')
})
