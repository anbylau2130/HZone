// cherry 日志编辑器回归：官方 demo 工具栏、上传回填、预览开关、katex/图表、全屏还原
import { test, expect } from '@playwright/test'
import { loginViaUi, registerAndVerifyViaApi } from './helpers'

const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64'
)

test('cherry editor: demo toolbar + uploads + formula/chart + preview toggle + fullscreen', async ({ page }) => {
  const user = await registerAndVerifyViaApi('CherryT')
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })

  // ② 全宽
  const panelBox = await page.locator('.journal-panel').boundingBox()
  console.log('journal panel width:', panelBox?.width, 'at 1280 viewport')

  // ④ demo 工具栏：一级按钮（cherry class 用 iconName，大小写敏感）
  for (const name of [
    'bold',
    'italic',
    'strike',
    'size',
    'color',
    'header',
    'ol',
    'ul',
    'checklist',
    'tips',
    'timeline',
    'align',
    'insertFlow',
    'insertFormula',
    'insert',
    'insertChart',
    'insertLineChart',
    'command',
    'search',
    'previewClose',
    'fullscreen',
    'wordCount',
  ]) {
    await expect(page.locator(`.cherry-toolbar-${name}`).first()).toBeVisible()
  }
  // 单行
  const toolbarH = await page
    .locator('.cherry-toolbar')
    .evaluate((el) => el.offsetHeight)
  console.log('toolbar height:', toolbarH)
  expect(toolbarH).toBeLessThanOrEqual(50)

  // strike 折叠组：5 项（strikethrough/underline/sub/sup/ruby；insertVideo 已移入 insert 组）
  await page.locator('.toolbar-left .cherry-toolbar-strike').click()
  await expect(page.locator('.cherry-dropdown[name="strikethrough"] .cherry-dropdown-item').first()).toBeVisible()
  console.log('strike fold items:', await page.locator('.cherry-dropdown[name="strikethrough"] .cherry-dropdown-item').count())
  await page.keyboard.press('Escape')

  // insert 折叠组：insertImage/audio/insertVideo/link/hr/br/code/(inlineCode)/formula/toc/table
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  console.log('insert fold items:', await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').count())

  // ① 插图（自定义 insertImage，规避原生第二笔插入缺陷）：文件框 → 上传 → 回填
  // waitForEvent 必须先于 click 注册（文件框在 click 动作内同步弹出）
  const imgChooser = page.waitForEvent('filechooser', { timeout: 3_000 }).catch(() => null)
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first().click()
  const chooser1 = await imgChooser
  expect(chooser1).toBeTruthy()
  await chooser1!.setFiles({ name: 'pic.png', mimeType: 'image/png', buffer: PNG })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/', { timeout: 15_000 })
  console.log('IMAGE upload+insert OK')

  // ① 自定义 insertVideo（insert 折叠第 3 项）→ <video> HTML 直插（第二笔插入不串台）
  await page.locator('.toolbar-left .cherry-toolbar-insert').click() // 首次子项点击后下拉已收起，重开
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  const vidChooser = page.waitForEvent('filechooser', { timeout: 3_000 }).catch(() => null)
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(2).click()
  const chooser2 = await vidChooser
  expect(chooser2).toBeTruthy()
  await chooser2!.setFiles({
    name: 'clip.mp4',
    mimeType: 'video/mp4',
    buffer: Buffer.from('00 00 00 18 66 74 79 70 6d 70 34'.split(' ').map((h) => parseInt(h, 16))),
  })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video', {
    timeout: 15_000,
  })
  console.log('custom VIDEO upload+insert OK')
  await expect(page.locator('.cherry-previewer video').first()).toBeVisible({ timeout: 10_000 })
  console.log('previewer renders video element OK')

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

  // 公式离线渲染（katex externals）
  const cm = page.locator('.cherry-editor .cm-content')
  await cm.click()
  await page.keyboard.type('公式 $E=mc^2$ 测试')
  await expect(page.locator('.cherry-previewer .katex')).toBeVisible({ timeout: 10_000 })
  console.log('katex formula renders offline: OK')

  // 表格转图表离线渲染（window.echarts）
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
})
