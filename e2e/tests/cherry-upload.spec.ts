// 最高优先级验证：真实手势链（trusted mouse 事件）下文件框必弹——上传全流程逐现象验证
// 1280 + 375 双视口。Chromium 的 filechooser 事件只在内部"用户激活检查"通过后派发；
// 激活缺失时只打 "File chooser dialog can only be shown with a user activation" 警告不发事件。
// 工具栏为官方 demo 版：图片/视频在「插入」折叠组（insertImage 第 1 项、insertVideo 第 3 项，均自定义菜单）。
import { test, expect } from '@playwright/test'
import { loginViaUi, registerAndVerifyViaApi } from './helpers'

const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64'
)
const MP4 = Buffer.from('00 00 00 18 66 74 79 70 6d 70 34'.split(' ').map((h) => parseInt(h, 16)))

async function setup(page: import('@playwright/test').Page, width: number) {
  const user = await registerAndVerifyViaApi('GestureT')
  await page.setViewportSize({ width, height: 900 })
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })
}

async function rawClick(page: import('@playwright/test').Page, locator: import('@playwright/test').Locator) {
  const box = await locator.boundingBox()
  if (!box) throw new Error('no bounding box')
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down()
  await page.mouse.up()
}

for (const width of [1280, 375]) {
  test(`gesture upload flow @ ${width}px`, async ({ page }) => {
    await setup(page, width)
    const activationWarnings: string[] = []
    page.on('console', (m) => {
      if (/user activation/i.test(m.text())) activationWarnings.push(m.text())
    })

    // ---- ① 原生视频（insert 折叠第 3 项，demo 同款）
    await rawClick(page, page.locator('.cherry-toolbar-insert'))
    await expect(page.locator('.cherry-dropdown .cherry-dropdown-item').first()).toBeVisible()
    console.log(`[${width}] ①insert 下拉可见`)
    const vidChooser = page.waitForEvent('filechooser', { timeout: 5_000 }).catch(() => null)
    await rawClick(page, page.locator('.cherry-dropdown .cherry-dropdown-item').nth(2))
    const chooserV = await vidChooser
    console.log(`[${width}] ②视频文件框: ${chooserV ? '弹出(trusted 手势)' : '未弹出'}`)
    console.log(`[${width}] ②激活警告: ${activationWarnings.length ? activationWarnings.join(';') : '无（手势链完整）'}`)
    expect(chooserV, '文件框必须弹出（否则真实浏览器同样不弹）').toBeTruthy()
    await chooserV!.setFiles({ name: 'clip.mp4', mimeType: 'video/mp4', buffer: MP4 })
    await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video', {
      timeout: 15_000,
    })
    console.log(`[${width}] ③视频回填: <video> HTML 已插入编辑区`)
    await expect(page.locator('.cherry-previewer video').first()).toBeVisible({ timeout: 10_000 })
    console.log(`[${width}] ④预览即时渲染: <video> 可见`)

    // ---- ② 图片（insert 折叠第 1 项）
    await rawClick(page, page.locator('.cherry-toolbar-insert'))
    await expect(page.locator('.cherry-dropdown .cherry-dropdown-item').first()).toBeVisible()
    const imgChooser = page.waitForEvent('filechooser', { timeout: 5_000 }).catch(() => null)
    await rawClick(page, page.locator('.cherry-dropdown .cherry-dropdown-item').first())
    const chooserI = await imgChooser
    console.log(`[${width}] ⑤图片文件框: ${chooserI ? '弹出(trusted 手势)' : '未弹出'}`)
    expect(chooserI).toBeTruthy()
    await chooserI!.setFiles({ name: 'pic.png', mimeType: 'image/png', buffer: PNG })
    await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/', { timeout: 15_000 })
    console.log(`[${width}] ⑥图片回填: ![pic.png](/media/…) 已插入编辑区`)
    await expect(page.locator('.cherry-previewer img').first()).toBeVisible({ timeout: 10_000 })
    console.log(`[${width}] ⑦预览即时渲染: 图片可见`)

    // 无 user-activation 警告 = Chromium 激活检查通过 = 真实浏览器必然弹 OS 文件框
    expect(activationWarnings, '不得出现 user activation 警告').toEqual([])
  })
}
