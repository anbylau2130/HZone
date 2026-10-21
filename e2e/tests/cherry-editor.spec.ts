// cherry 日志编辑器回归：上传回填、全宽、预览开关记忆、官方默认工具栏、公式离线渲染、全屏还原
import { test, expect } from '@playwright/test'
import { loginViaUi, registerAndVerifyViaApi } from './helpers'

test('cherry editor: uploads + default toolbar + formula + preview toggle + fullscreen', async ({ page }) => {
  const user = await registerAndVerifyViaApi('CherryT')
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })

  // ② 全宽
  const panelBox = await page.locator('.journal-panel').boundingBox()
  console.log('journal panel width:', panelBox?.width, 'at 1280 viewport')

  // ④ 显式全量工具栏：一级按钮可见（cherry class 用 iconName，大小写敏感）
  for (const name of [
    'bold',
    'italic',
    'strike',
    'underline',
    'sub',
    'sup',
    'header',
    'color',
    'list',
    'ol',
    'ul',
    'checklist',
    'blockquote',
    'codeBlock',
    'table',
    'line',
    'image',
    'video',
    'link',
    'undo',
    'redo',
    'search',
    'settings',
    'chevrons',
    'wordCount',
    'switchPreview',
    'command',
    'fullscreen',
  ]) {
    await expect(page.locator(`.cherry-toolbar-${name}`).first()).toBeVisible()
  }

  // moreTools 折叠分组（ruby/detail/graph/formula/toc 等低频项）
  await page.locator('.cherry-toolbar-chevrons').click()
  await expect(page.locator('.cherry-dropdown .cherry-dropdown-item').first()).toBeVisible()
  console.log('moreTools fold items:', await page.locator('.cherry-dropdown .cherry-dropdown-item').count())
  await page.keyboard.press('Escape')

  // ① 插图（一级按钮）→ 文件框 → 上传 → 回填
  // 注意 waitForEvent 必须先于 click（文件框在 click 动作内同步弹出）
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

  // ③ 预览开关
  await expect(page.locator('.cherry-previewer')).toBeVisible()
  await page.locator('.preview-toggle').click()
  await expect(page.locator('.cherry-previewer')).toBeHidden()
  const editorWidth = await page.locator('.cherry-editor').evaluate((el) => el.getBoundingClientRect().width)
  console.log('editor width with preview off:', editorWidth)
  await page.reload()
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })
  await expect(page.locator('.cherry-previewer')).toBeHidden()
  console.log('preview off persisted after reload')
  await page.locator('.preview-toggle').click()
  await expect(page.locator('.cherry-previewer')).toBeVisible()

  // 公式离线渲染（katex externals）：输入行内公式 → 预览出 .katex
  const cm = page.locator('.cherry-editor .cm-content')
  await cm.click()
  await page.keyboard.type('公式 $E=mc^2$ 测试')
  await expect(page.locator('.cherry-previewer .katex')).toBeVisible({ timeout: 10_000 })
  console.log('katex formula renders offline: OK')

  // 表格转图表离线渲染（window.echarts + cherry 内置 table-chart 插件；图表参数占首个表头单元格）
  await page.keyboard.press('Enter')
  await page.keyboard.type('| :bar: | b |\n| --- | --- |\n| 1 | 2 |')
  await expect(page.locator('.cherry-previewer svg').first()).toBeVisible({ timeout: 15_000 })
  console.log('table chart renders offline: OK')

  // ⑤ 全屏还原闭环
  await page.keyboard.press('Escape')
  await page.locator('.cherry-toolbar-fullscreen').click()
  const fs = page.locator('.cherry.fullscreen')
  await expect(fs).toBeVisible()
  expect(Number(await fs.evaluate((el) => getComputedStyle(el).zIndex))).toBeGreaterThan(999)
  await page.locator('.cherry-toolbar-fullscreen').click()
  await expect(page.locator('.cherry.fullscreen')).toHaveCount(0)
  console.log('fullscreen enter+restore OK')

  // ① 插视频（一级按钮）：文件框 → 上传 → 回填 <video>
  const vidChooser = page.waitForEvent('filechooser', { timeout: 3_000 })
  await page.locator('.cherry-toolbar-video').first().click()
  const chooser2 = await vidChooser
  await chooser2.setFiles({
    name: 'clip.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.from('00 00 00 18 66 74 79 70 6d 70 34'.split(' ').map((h) => parseInt(h, 16))),
  })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video', { timeout: 15_000 })
  console.log('VIDEO upload+insert OK')
})
