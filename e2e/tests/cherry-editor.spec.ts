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

  // insert 折叠组：insertImage/insertAudio/insertVideo/link/hr/br/code/(inlineCode)/formula/toc/table
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
  // 连续插入互吞回归：fire() 回填会把上一笔插入设为选区，下一笔未折叠会整段替换掉
  //（图片→音频→视频→流程图连插互吞，2026-10-22 修复：afterClickCb 折叠选区）
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/')

  // ① 自定义 insertAudio（insert 折叠第 2 项）→ !audio[name](url) 语法回填 + 预览 <audio>
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  const audChooser = page.waitForEvent('filechooser', { timeout: 3_000 }).catch(() => null)
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(1).click()
  const chooser3 = await audChooser
  expect(chooser3).toBeTruthy()
  await chooser3!.setFiles({
    name: 'tone.mp3',
    mimeType: 'audio/mpeg',
    buffer: Buffer.from(`ID3${'x'.repeat(64)}`),
  })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('!audio[tone.mp3](', {
    timeout: 15_000,
  })
  console.log('custom AUDIO upload+insert OK')
  await expect(page.locator('.cherry-previewer audio').first()).toBeVisible({ timeout: 10_000 })
  console.log('previewer renders audio element OK')
  // 连续插入互吞回归（图→视→音三笔共存；后面 preview-toggle 会 reload 清空草稿，存活断言须在其前）
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('![pic.png](/media/')
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('!audio[tone.mp3](')
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('<video')

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

  // mermaid 流程图离线渲染（usePlugin 显式注入本地 mermaid 包；graph 下拉插流程图模板）
  await page.locator('.cherry-toolbar-insertChart').click() // graph 菜单按钮（icon=insertChart）
  await expect(page.locator('.cherry-dropdown[name="graph"] .cherry-dropdown-item').first()).toBeVisible()
  await page.locator('.cherry-dropdown[name="graph"] .cherry-dropdown-item').first().click()
  await expect(page.locator('.cherry-previewer figure[data-type="mermaid"] svg').first()).toBeVisible({
    timeout: 20_000,
  })
  console.log('mermaid flowchart renders offline: OK')

  // drawIO（官方 drawio_demo 自托管集成）：弹层 iframe 起编辑器 → setData 注入 xml →
  // 确定(getData) 导出 PNG dataURL → cherry 插入带 data-type=drawio 的图片语法。
  // 先把光标移到文末换行：上一步 mermaid 模板的光标还在代码围栏内，直插会被围栏吞成代码文本
  await page.locator('.cherry-editor .cm-content').click()
  await page.keyboard.press('Control+End')
  await page.keyboard.press('Enter')
  await page.keyboard.press('Enter')
  await page.locator('.cherry-toolbar-draw\\.io').click()
  await expect(page.locator('.cherry-dialog')).toBeVisible({ timeout: 10_000 })
  const dioFrameEl = page.locator('.cherry-dialog iframe')
  await page.waitForFunction(
    (fr) => !!(fr && fr.contentWindow && (fr.contentWindow as unknown as { editorUIInstance?: unknown }).editorUIInstance),
    await dioFrameEl.elementHandle(),
    { timeout: 30_000 }
  )
  console.log('drawio editor booted in dialog iframe OK')
  await page.evaluate(
    (fr) => {
      ;(fr as HTMLIFrameElement).contentWindow!.postMessage(
        {
          eventName: 'setData',
          value:
            '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/><mxCell id="2" value="hello" style="rounded=1;whiteSpace=wrap;html=1;" vertex="1" parent="1"><mxGeometry x="200" y="200" width="120" height="60" as="geometry"/></mxCell></root></mxGraphModel>',
        },
        '*'
      )
    },
    await dioFrameEl.elementHandle()
  )
  await page.waitForFunction(
    (fr) => {
      // mxgraph 导入的矩形（120×60 圆角）；空画布无 rect（仅背景），rect 出现即 xml 已导入
      const rect = (fr as HTMLIFrameElement).contentDocument?.querySelector('.geDiagramContainer svg rect')
      return !!rect
    },
    await dioFrameEl.elementHandle(),
    { timeout: 15_000 }
  )
  console.log('drawio setData imported graph xml OK')
  await page.locator('.cherry-dialog--sure').click()
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('data:image/png;base64', { timeout: 20_000 })
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('data-type=drawio')
  await expect(page.locator('.cherry-dialog')).toBeHidden()
  await expect(page.locator('.cherry-previewer img[data-type="drawio"]').first()).toBeVisible({ timeout: 15_000 })
  console.log('drawio export->insert roundtrip OK')
  // 重载后的草稿里 mermaid 模板与 drawio 插入共存（媒体段在 reload 前已另行断言）
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('```mermaid')
  await expect(page.locator('.cherry-editor .cm-content')).toContainText('data-type=drawio')

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
