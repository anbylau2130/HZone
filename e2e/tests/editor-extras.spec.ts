// 本轮四件套回归：401 死角修复（守卫+无 token 401 跳登录）、日志列表卡片/表格切换、
// 图片 alt 尺寸语法读侧 parity、编辑器三新功能（打开本地 .md / 导出菜单 / 插入文件附件）
import { expect, test } from '@playwright/test'
import * as fs from 'fs'
import * as path from 'path'
import {
  API_BASE,
  enterJournalEditMode,
  loginViaUi,
  registerAndVerifyViaApi,
  toggleJournalPreview,
  uniButton,
} from './helpers'

const RESULT_DIR = path.resolve(__dirname, '../test-results/editor-extras')

async function createJournalViaApi(token: string, title: string, markdown: string): Promise<string> {
  const res = await fetch(`${API_BASE}/contents/journals`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`
    },
    body: JSON.stringify({ title, markdown, recipientIds: [] })
  })
  expect([200, 201]).toContain(res.status)
  return ((await res.json()) as { id: string }).id
}

test('guest deep-link to publish redirects to login (no dead-end 401)', async ({ page }) => {
  // 未登录（全新 context 无 token）直接深链发布页：守卫应立即踢回登录页，
  // 而不是停在编辑页发布时吃 401 死循环
  await page.goto('/#/pages/publish/publish?kind=journal')
  await expect(uniButton(page, '登录')).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/pages\/login\/login/)
})

test('journals list toggles card/table view, edit jumps to publish', async ({ page }) => {
  const user = await registerAndVerifyViaApi('ViewUser')
  // 两篇：先造旧文再造新文（列表按时间倒序，阅读视图默认最新=后造的这篇）
  await createJournalViaApi(user.token, '更早的一篇', '旧文正文')
  await createJournalViaApi(user.token, '表格视图测试', '正文内容')

  await loginViaUi(page, user)
  await page.goto('/#/pages/journals/journals')
  // 默认即阅读视图（无记忆时）：连续信息流，最新一篇在最上
  await expect(page.locator('.r-title').first()).toHaveText('表格视图测试', { timeout: 15_000 })

  // 切卡片：卡可见（data-view 属性定位，语言无关）
  await page.locator('.vt[data-view="card"]').click()
  await expect(page.locator('.card').first()).toBeVisible({ timeout: 15_000 })

  // 切表格：表头 + 数据行 + 字数/操作列
  await page.locator('.vt[data-view="table"]').click()
  await expect(page.locator('.table .thead')).toBeVisible()
  await expect(page.locator('.trow .c-title', { hasText: '表格视图测试' })).toBeVisible()

  // 表格"编辑"直达 publish 编辑页（与创建同一页面）；新规则默认预览模式，先切「编辑」
  await page.locator('.trow .op', { hasText: '编辑' }).first().click()
  await enterJournalEditMode(page)
  await expect(page.locator('.j-title input')).toHaveValue('表格视图测试')

  // 回列表切阅读视图：连续信息流——两篇竖向堆叠，最新在上，无翻页控件
  await page.goto('/#/pages/journals/journals')
  await page.locator('.vt[data-view="read"]').click()
  await expect(page.locator('.r-title', { hasText: '表格视图测试' })).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.r-title', { hasText: '更早的一篇' })).toBeVisible({ timeout: 15_000 })
  await expect(page.locator('.r-count')).toHaveCount(0)

  // 视图偏好 localStorage 记忆：重进仍为阅读
  await page.goto('/#/pages/journals/journals')
  await expect(page.locator('.r-title').first()).toBeVisible({ timeout: 15_000 })
})

test('image alt size syntax renders at requested size in reader view', async ({ page }) => {
  const user = await registerAndVerifyViaApi('SizeUser')
  // 最新日志即阅读视图首篇（本用户仅此一篇），无需按 id 定位
  await createJournalViaApi(
    user.token,
    '图片尺寸',
    '前文\n\n![配图 #120px#60px](/media/nonexistent.jpg)\n\n后文'
  )

  await loginViaUi(page, user)
  // 详情页已移除：改在日志页阅读视图（最新一篇在最上）断言渲染
  await page.goto('/#/pages/journals/journals')
  await expect(page.locator('.r-title', { hasText: '图片尺寸' })).toBeVisible({ timeout: 15_000 })
  // 读侧统一 cherry previewOnly（用户决策）：cherry 预览原生解析 alt #尺寸# 语法并套
  // width/height（与编辑器预览完全一致）；alt 文本保留原 token（cherry 语义，非泄漏）
  const img = page.locator('.cherry-previewer img').first()
  await expect(img).toBeVisible()
  await expect(img).toHaveAttribute('alt', /配图/)
  await expect(img).toHaveAttribute('style', /width:\s*120px/)
  await expect(img).toHaveAttribute('style', /height:\s*60px/)
})

test('editor extras: open local md, export menu, insert file attachment', async ({ page }) => {
  // 多步重流程（两次开文件+确认弹窗+导出+上传+发布+下载），60s 套件默认不够
  test.setTimeout(150_000)
  const user = await registerAndVerifyViaApi('ExtraUser')
  await loginViaUi(page, user)
  await page.goto('/#/pages/publish/publish?kind=journal')
  // 新规则：发布页默认预览模式——先切「编辑」
  await enterJournalEditMode(page)
  const cm = page.locator('.cherry-editor .cm-content')

  fs.mkdirSync(RESULT_DIR, { recursive: true })
  const mdFile = path.join(RESULT_DIR, 'local-doc.md')
  fs.writeFileSync(mdFile, '# 本地导入\n\n本地导入正文第一段。', 'utf8')

  // ① 打开本地 .md：空编辑器直接整篇替换（无确认弹窗）
  // 注：cherry 自定义菜单的工具栏按钮类名取图标名（folder-open），不是菜单名
  const openLocalBtn = page.locator('.cherry-toolbar-folder-open')
  const [chooser1] = await Promise.all([
    page.waitForEvent('filechooser'),
    openLocalBtn.click()
  ])
  await chooser1.setFiles(mdFile)
  await expect(cm).toContainText('本地导入正文第一段', { timeout: 10_000 })

  // 已有内容时再打开 → 确认弹窗（替换/取消）
  const mdFile2 = path.join(RESULT_DIR, 'local-doc-2.md')
  fs.writeFileSync(mdFile2, '第二篇正文。', 'utf8')
  const [chooser2] = await Promise.all([
    page.waitForEvent('filechooser'),
    openLocalBtn.click()
  ])
  await chooser2.setFiles(mdFile2)
  // uni.showModal 确认按钮（已显式传文案 common.confirm=确定；class 定位与文案解耦）
  const confirmBtn = page.locator('.uni-modal__btn_primary')
  await expect(confirmBtn).toBeVisible({ timeout: 10_000 })
  await confirmBtn.click()
  await expect(cm).toContainText('第二篇正文', { timeout: 10_000 })

  // ② 导出菜单（官方内置，全客户端）：按钮 + 子菜单项。
  // 已知竞态：确认弹窗关闭的 DOM 移除偶发触发 document click，把刚打开的
  // dropdown 立即关掉——自愈重开一次（断言强度不变，dropdown 必须可见）
  const exportItem = page.locator('.cherry-dropdown[name="export"] .cherry-dropdown-item').first()
  await page.locator('.cherry-toolbar-export').click()
  await page.waitForTimeout(300)
  if (!(await exportItem.isVisible().catch(() => false))) {
    await page.locator('.cherry-toolbar-export').click()
  }
  await expect(
    page.locator('.cherry-dropdown[name="export"] .cherry-dropdown-item').first()
  ).toBeVisible({ timeout: 10_000 })

  // ③ 插入文件附件：txt → 上传 → [名字](/media/..) 链接回填
  const txt = path.join(RESULT_DIR, '附件说明.txt')
  fs.writeFileSync(txt, 'attachment payload', 'utf8')
  await page.locator('.toolbar-left .cherry-toolbar-insert').click()
  const [chooser3] = await Promise.all([
    page.waitForEvent('filechooser'),
    page.locator('.cherry-dropdown[name="insert"] .cherry-dropdown-item').nth(3).click()
  ])
  await chooser3.setFiles(txt)
  await expect(cm).toContainText('[附件说明.txt](/media/', { timeout: 15_000 })
  // 预览即时断言附件链接渲染（桌面双栏常显 no-op；移动端切预览，后续发布在预览态也能提交）
  await toggleJournalPreview(page)
  await expect(page.locator('.cherry-previewer a[href^="/media/"]').first()).toBeVisible()

  // 发布 → 详情页：附件链接在，点击走授权下载（download 事件 + 原文件名）
  await page.locator('.j-title input').fill('编辑器扩展测试')
  await uniButton(page, '发布').click()
  await page.waitForTimeout(1_000)
  const mine = await fetch(`${API_BASE}/me/contents?kind=journal&offset=0&limit=1`, {
    headers: { Authorization: `Bearer ${user.token}` }
  })
  const journalId = ((await mine.json()) as { items: { id: string }[] }).items[0].id
  // 详情页已移除：改在日志页阅读视图断言附件链接点击下载
  await page.goto('/#/pages/journals/journals')
  await expect(page.locator('.r-title', { hasText: '编辑器扩展测试' })).toBeVisible({ timeout: 15_000 })
  const link = page.locator('.cherry-previewer a[href^="/media/"]', { hasText: '附件说明.txt' })
  await expect(link).toBeVisible({ timeout: 15_000 })
  const [download] = await Promise.all([page.waitForEvent('download'), link.click()])
  expect(download.suggestedFilename()).toBe('附件说明.txt')
})
