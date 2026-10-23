// 浏览器自行测试（browser tour）：真实 UI 走完"发布日志（图/音/视/流程图/drawio）→ 发布
// → 详情页阅读渲染"全链路，每步截图供人工目检。作为回归用例保留。
import { test, expect } from '@playwright/test'
import { loginViaUi, registerAndVerifyViaApi, uniButton } from './helpers'

const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==',
  'base64'
)
const MP3 = Buffer.from(`ID3${'x'.repeat(64)}`)
const MP4 = Buffer.from('00 00 00 18 66 74 79 70 6d 70 34'.split(' ').map((h) => parseInt(h, 16)))
const DRAWIO_XML =
  '<mxGraphModel><root><mxCell id="0"/><mxCell id="1" parent="0"/><mxCell id="2" value="browser tour" style="rounded=1;whiteSpace=wrap;html=1;" vertex="1" parent="1"><mxGeometry x="200" y="200" width="140" height="60" as="geometry"/></mxCell></root></mxGraphModel>'

test('browser tour: journal with image/audio/video/mermaid/drawio renders on detail page', async ({ page }) => {
  const shot = (name: string) =>
    page.screenshot({ path: `test-results/browser-tour/${name}.png`, fullPage: true })

  const user = await registerAndVerifyViaApi('TourUser')
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  await page.waitForSelector('.cherry-editor .cm-content', { timeout: 20_000 })

  // 标题 + 正文开头（uni-input 包装，fill 打内层 input）
  const titleInput = page.locator('.j-title input')
  await titleInput.fill('浏览器实测：多媒体与画图日志')
  const cm = page.locator('.cherry-editor .cm-content')
  await cm.click()
  await page.keyboard.type('这是一篇浏览器实测日志，覆盖插图/音频/视频/流程图/drawio。\n\n')

  // ---- 插入图片（insert 组第 1 项）----
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  let chooser = page.waitForEvent('filechooser', { timeout: 3_000 })
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(0).click()
  await (await chooser).setFiles({ name: 'tour.png', mimeType: 'image/png', buffer: PNG })
  await expect(cm).toContainText('![tour.png](/media/', { timeout: 15_000 })

  // ---- 插入音频（第 2 项）----
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  chooser = page.waitForEvent('filechooser', { timeout: 3_000 })
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(1).click()
  await (await chooser).setFiles({ name: 'tone.mp3', mimeType: 'audio/mpeg', buffer: MP3 })
  await expect(cm).toContainText('!audio[tone.mp3](', { timeout: 15_000 })

  // ---- 插入视频（第 3 项）----
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  await expect(page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').first()).toBeVisible()
  chooser = page.waitForEvent('filechooser', { timeout: 3_000 })
  await page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(2).click()
  await (await chooser).setFiles({ name: 'clip.mp4', mimeType: 'video/mp4', buffer: MP4 })
  await expect(cm).toContainText('<video', { timeout: 15_000 })
  await shot('01-editor-media-inserted')

  // ---- mermaid 流程图（graph 下拉第 1 项模板）----
  await page.locator('.cherry-toolbar-insertChart').click()
  await expect(page.locator('.cherry-dropdown[name="graph"] .cherry-dropdown-item').first()).toBeVisible()
  await page.locator('.cherry-dropdown[name="graph"] .cherry-dropdown-item').first().click()
  await expect(page.locator('.cherry-previewer figure[data-type="mermaid"] svg').first()).toBeVisible({
    timeout: 20_000,
  })

  // ---- drawio：弹层 → 注入 xml → 确定 → base64 插入 ----
  await page.keyboard.press('Control+End')
  await page.keyboard.press('Enter')
  await page.locator('.cherry-toolbar-draw\\.io').click()
  await expect(page.locator('.cherry-dialog')).toBeVisible({ timeout: 10_000 })
  await shot('02-drawio-dialog-open')
  const frameEl = page.locator('.cherry-dialog iframe')
  await page.waitForFunction(
    (f) => !!(f && f.contentWindow && (f.contentWindow as unknown as { editorUIInstance?: unknown }).editorUIInstance),
    await frameEl.elementHandle(),
    { timeout: 30_000 }
  )
  await page.evaluate(
    ({ f, xml }) => {
      ;(f as HTMLIFrameElement).contentWindow!.postMessage({ eventName: 'setData', value: xml }, '*')
    },
    { f: await frameEl.elementHandle(), xml: DRAWIO_XML }
  )
  await page.waitForFunction(
    (f) => !!(f as HTMLIFrameElement).contentDocument?.querySelector('.geDiagramContainer svg rect'),
    await frameEl.elementHandle(),
    { timeout: 15_000 }
  )
  await shot('03-drawio-graph-loaded')
  await page.locator('.cherry-dialog--sure').click()
  await expect(cm).toContainText('data:image/png;base64', { timeout: 20_000 })
  await expect(page.locator('.cherry-previewer img[data-type="drawio"]').first()).toBeVisible({ timeout: 15_000 })
  await shot('04-editor-all-content')

  // ---- 发布 ----
  await uniButton(page, '发布').click()
  await page.waitForTimeout(2_500) // toast + navigateBack

  // ---- 拿到新日志 id，进详情页 ----
  const journalId = await page.evaluate(async () => {
    const token = localStorage.getItem('hz_token') ?? ''
    const r = await fetch('/api/me/contents?kind=journal&offset=0&limit=1', {
      headers: { Authorization: `Bearer ${token}` },
    })
    const j = await r.json()
    return j.items?.[0]?.id ?? ''
  })
  expect(journalId).toBeTruthy()
  const savedBody = await page.evaluate(async (id) => {
    const token = localStorage.getItem('hz_token') ?? ''
    const r = await fetch(`/api/contents/${id}`, { headers: { Authorization: `Bearer ${token}` } })
    const j = await r.json()
    return j.markdownBody ?? ''
  }, journalId)
  console.log('SAVED BODY >>>', JSON.stringify(savedBody))
  await page.goto(`/#/pages/detail/detail?id=${journalId}`)
  await expect(page.locator('.cherry-previewer')).toBeVisible({ timeout: 15_000 })
  await page.waitForTimeout(3_000) // 等 mermaid 异步渲染 + /media 鉴权换 blob

  // 详情页断言：mermaid 图 / 上传图鉴权可载 / drawio 图 / 音频控件 / 视频控件
  await expect(page.locator('.cherry-previewer figure[data-type="mermaid"] svg').first()).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.cherry-previewer img[src^="blob:"]').first()).toBeVisible({ timeout: 10_000 })
  await expect(page.locator('.cherry-previewer img[data-type="drawio"]').first()).toBeVisible({ timeout: 10_000 })
  const audio = page.locator('.cherry-previewer audio[controls]')
  await expect(audio.first()).toBeVisible({ timeout: 10_000 })
  expect(await audio.getAttribute('src')).toMatch(/^blob:/)
  await expect(page.locator('.cherry-previewer video[controls]').first()).toBeVisible({ timeout: 10_000 })
  await shot('05-detail-page-full')

  // 图片真实可解码（鉴权换 blob 后 naturalWidth > 0，防"元素在但 401 断图"）
  const imgOk = await page.evaluate(() =>
    Array.from(document.querySelectorAll('.cherry-previewer img')).map((i) => (i as HTMLImageElement).naturalWidth > 0)
  )
  console.log('detail images loaded:', imgOk)
  expect(imgOk.every(Boolean)).toBe(true)
})
