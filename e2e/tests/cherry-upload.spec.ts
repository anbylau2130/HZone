// 最高优先级验证：真实手势链（trusted mouse 事件）下文件框必弹——上传全流程逐现象验证
// 1280 + 375 双视口。Chromium 对 filechooser 事件的前提是激活检查通过；若手势链断，
// Chromium 会打 "File chooser dialog can only be shown with a user activation" 警告且
// 不发 filechooser——本用例同时断言两者，构成"真实浏览器会弹框"的证据链。
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

/** 原始 trusted 鼠标事件点击某 locator 中心（不经 locator.click 的 actionability 魔法） */
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

    // ---- ① 视频按钮（一级按钮）：raw mouse → 文件框 → 上传 → 即渲染
    // waitForEvent 必须先于点击注册：文件框在 click 动作内同步弹出
    const vidChooser = page.waitForEvent('filechooser', { timeout: 5_000 }).catch(() => null)
    await rawClick(page, page.locator(".cherry-toolbar-video[title='insertVideo']"))
    const chooserV = await vidChooser
    console.log(`[${width}] ①视频按钮 raw 点击（一级按钮，无下拉中转）`)
    console.log(`[${width}] ②视频文件框:`, chooserV ? '弹出(trusted 手势)' : '未弹出')
    console.log(`[${width}] ②激活警告:`, activationWarnings.length ? activationWarnings : '无（手势链完整）')
    expect(chooserV, '文件框必须弹出（否则真实浏览器同样不弹）').toBeTruthy()
    await chooserV!.setFiles({ name: 'clip.mp4', mimeType: 'video/mp4', buffer: MP4 })
    await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video', { timeout: 15_000 })
    console.log(`[${width}] ③视频上传回填: <video> 已插入编辑区`)
    await expect(page.locator('.cherry-previewer video').first()).toBeVisible({ timeout: 10_000 })
    console.log(`[${width}] ④预览即时渲染: <video> 可见`)

    // ---- ② 图片按钮（一级按钮）：raw mouse → 文件框 → 上传 → 即渲染
    const imgChooser = page.waitForEvent('filechooser', { timeout: 5_000 }).catch(() => null)
    await rawClick(page, page.locator('.cherry-toolbar-image'))
    const chooserI = await imgChooser
    console.log(`[${width}] ⑤图片文件框:`, chooserI ? '弹出(trusted 手势)' : '未弹出')
    expect(chooserI).toBeTruthy()
    await chooserI!.setFiles({ name: 'pic.png', mimeType: 'image/png', buffer: PNG })
    await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/', { timeout: 15_000 })
    console.log(`[${width}] ⑥图片上传回填: ![pic.png](/media/…) 已插入`)
    await expect(page.locator('.cherry-previewer img').first()).toBeVisible({ timeout: 10_000 })
    console.log(`[${width}] ⑦预览即时渲染: 图片可见`)
    // 无 user-activation 警告 = Chromium 激活检查通过 = 真实浏览器必然弹 OS 文件框
    expect(activationWarnings, '不得出现 user activation 警告').toEqual([])
  })
}
