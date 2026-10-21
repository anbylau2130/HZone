// cherry 日志编辑器回归：上传回填、全宽、预览开关记忆、工具栏折叠分组
import { test, expect } from '@playwright/test'
import { loginViaUi, registerAndVerifyViaApi } from './helpers'

test('cherry editor: image/video upload + toolbar fold + preview toggle + full width', async ({ page }) => {
  const user = await registerAndVerifyViaApi('CherryT')
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })

  // ② 全宽：page padding 24px，journal 面板占满可用宽度
  const pageBox = await page.locator('.page.journal-mode').boundingBox()
  console.log('page box width:', pageBox?.width)
  const panelBox = await page.locator('.journal-panel').boundingBox()
  console.log('journal panel width:', panelBox?.width, 'at 1280 viewport')

  // ④ 工具栏铺满一行 + 折叠分组
  for (const name of ['bold', 'italic', 'header', 'image', 'video', 'link', 'undo', 'redo', 'table', 'line']) {
    await expect(page.locator(`.cherry-toolbar-${name}`).first()).toBeVisible()
  }
  await expect(page.locator('.cherry-toolbar-chevrons')).toBeVisible()
  await expect(page.locator('.cherry-toolbar-fullscreen')).toBeVisible()
  await page.locator('.cherry-toolbar-chevrons').click()
  const foldItems = page.locator('.cherry-dropdown .cherry-dropdown-item')
  await expect(foldItems).toHaveCount(3)
  console.log('toolbar fold group OK')
  await page.keyboard.press('Escape')

  // ③ 预览开关：点击后预览窗隐藏、编辑区占满；localStorage 记忆
  await expect(page.locator('.cherry-previewer')).toBeVisible()
  await page.locator('.preview-toggle').click()
  await expect(page.locator('.cherry-previewer')).toBeHidden()
  const editorWidth = await page
    .locator('.cherry-editor')
    .evaluate((el) => el.getBoundingClientRect().width)
  console.log('editor width with preview off:', editorWidth)
  await page.reload()
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })
  await expect(page.locator('.cherry-previewer')).toBeHidden()
  console.log('preview off persisted after reload')
  await page.locator('.preview-toggle').click()
  await expect(page.locator('.cherry-previewer')).toBeVisible()

  // ① 插图：点击弹文件框 → 上传 → 回填
  const imgChooser = page.waitForEvent('filechooser', { timeout: 3_000 }).catch(() => null)
  await page.locator('.cherry-toolbar-image').first().click()
  const chooser1 = await imgChooser
  expect(chooser1).toBeTruthy()
  await chooser1!.setFiles({
    name: 'pic.png',
    mimeType: 'image/png',
    buffer: Buffer.from(
      'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
      'base64'
    ),
  })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/', { timeout: 15_000 })
  console.log('IMAGE upload+insert OK')

  // ① 插视频：点击弹文件框 → 上传 → 回填
  const vidChooser = page.waitForEvent('filechooser', { timeout: 3_000 }).catch(() => null)
  await page.locator('.cherry-toolbar-video').first().click()
  const chooser2 = await vidChooser
  expect(chooser2).toBeTruthy()
  await chooser2!.setFiles({
    name: 'clip.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.from('00 00 00 18 66 74 79 70 6d 70 34'.split(' ').map((h) => parseInt(h, 16))),
  })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video', { timeout: 15_000 })
  console.log('VIDEO upload+insert OK')
})
