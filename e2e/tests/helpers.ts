import { expect, type Locator, type Page } from '@playwright/test'
import * as fs from 'fs'
import * as path from 'path'

// API 与 H5 均为本地进程（playwright.config webServer 自动拉起）；后端 data 目录
// （dotnet run 工作目录 src/HZone.Api/data）含 SQLite 与 outbox .eml。
export const API_BASE = 'http://localhost:5000'
export const DATA_DIR = path.resolve(__dirname, '../../src/HZone.Api/data')
export const OUTBOX_DIR = path.join(DATA_DIR, 'outbox')

// 固定管理员凭据：实例未初始化时由 ensureInstanceInitialized 引导创建；
// 已初始化（含历史数据）时按同一凭据登录。
export const ADMIN_EMAIL = 'admin@hzone.test'
export const ADMIN_PASSWORD = 'Passw0rd!hzone'

const PASSWORD = 'UiPassw0rd!x'

export interface TestUser {
  email: string
  password: string
  nickname: string
  userId: string
  token: string
}

let emailCounter = 0

export function uniqueEmail(prefix: string): string {
  emailCounter += 1
  return `${prefix}-${Date.now()}-${emailCounter}@hzone.test`.toLowerCase()
}

// ---------- 纯 HTTP API（Node fetch，绕过浏览器上下文） ----------

async function api(
  method: string,
  pathName: string,
  body?: unknown,
  token?: string
): Promise<{ status: number; json: any }> {
  const res = await fetch(`${API_BASE}${pathName}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  const text = await res.text()
  let json: any = null
  try {
    json = text ? JSON.parse(text) : null
  } catch {
    json = text
  }
  return { status: res.status, json }
}

/** 实例未初始化时走 setup/admin 引导（幂等：已初始化直接返回）。 */
export async function ensureInstanceInitialized(): Promise<void> {
  const status = await api('GET', '/setup/status')
  if (status.json?.initialized) return
  const created = await api('POST', '/setup/admin', {
    email: ADMIN_EMAIL,
    password: ADMIN_PASSWORD,
    nickname: 'Admin',
  })
  if (created.status !== 201 && created.status !== 409) {
    throw new Error(`setup/admin failed: ${created.status} ${JSON.stringify(created.json)}`)
  }
}

async function adminToken(): Promise<string> {
  const res = await api('POST', '/auth/login', { email: ADMIN_EMAIL, password: ADMIN_PASSWORD })
  if (res.status !== 200) {
    throw new Error(`admin login failed: ${res.status} ${JSON.stringify(res.json)}`)
  }
  return res.json.token as string
}

/** 管理员创建一枚邀请码，返回明文码。 */
export async function createInviteCode(): Promise<string> {
  await ensureInstanceInitialized()
  const invite = await api('POST', '/invites', undefined, await adminToken())
  if (invite.status !== 201) {
    throw new Error(`create invite failed: ${JSON.stringify(invite.json)}`)
  }
  return invite.json.code as string
}

/** 从 outbox 最新匹配邮箱的 .eml 中提取 verify-email token（注册验证邮件落盘有延迟，轮询）。 */
export async function extractVerifyToken(email: string, timeoutMs = 10_000): Promise<string> {
  const deadline = Date.now() + timeoutMs
  const target = email.toLowerCase()
  for (;;) {
    if (fs.existsSync(OUTBOX_DIR)) {
      const eml = new DirectoryInfo(OUTBOX_DIR)
        .listEml()
        .find((f) => f.content.toLowerCase().includes(target))
      if (eml) {
        const match = eml.content.match(/verify-email\?token=([A-Za-z0-9]+)/)
        if (match) return match[1]
      }
    }
    if (Date.now() > deadline) {
      throw new Error(`no verify email for ${email} in ${OUTBOX_DIR}`)
    }
    await new Promise((r) => setTimeout(r, 300))
  }
}

// 简化的 .eml 读取（按文件名时间戳倒序 = 最新优先）
class DirectoryInfo {
  constructor(private dir: string) {}

  listEml(): { name: string; content: string }[] {
    return fs
      .readdirSync(this.dir)
      .filter((n) => n.endsWith('.eml'))
      .sort()
      .reverse()
      .map((name) => ({ name, content: fs.readFileSync(path.join(this.dir, name), 'utf8') }))
  }
}

/**
 * HTTP 注册→读 .eml token→验证→登录，返回可直接调 API 的用户。
 * 邮箱即账号：登录与找回一律用邮箱。
 */
export async function registerAndVerifyViaApi(nickname: string): Promise<TestUser> {
  await ensureInstanceInitialized()
  const email = uniqueEmail(nickname)
  const admin = await adminToken()

  const invite = await api('POST', '/invites', undefined, admin)
  if (invite.status !== 201) {
    throw new Error(`create invite failed: ${JSON.stringify(invite.json)}`)
  }
  const code = invite.json.code as string

  const register = await api('POST', '/auth/register', {
    inviteCode: code,
    email,
    password: PASSWORD,
    nickname,
  })
  if (register.status !== 201) {
    throw new Error(`register failed: ${JSON.stringify(register.json)}`)
  }
  const userId = register.json.userId as string

  const token = await extractVerifyToken(email)
  const verify = await api('POST', '/auth/verify-email', { token })
  if (verify.status !== 200) {
    throw new Error(`verify-email failed: ${JSON.stringify(verify.json)}`)
  }

  const login = await api('POST', '/auth/login', { email, password: PASSWORD })
  if (login.status !== 200) {
    throw new Error(`login failed: ${JSON.stringify(login.json)}`)
  }
  return { email, password: PASSWORD, nickname, userId, token: login.json.token as string }
}

/** A → B 好友申请，B 同意（双向好友）。 */
export async function makeFriends(a: TestUser, b: TestUser): Promise<void> {
  const req = await api('POST', '/friends/requests', { targetUserId: b.userId }, a.token)
  if (req.status !== 201) {
    throw new Error(`friend request failed: ${JSON.stringify(req.json)}`)
  }
  const incoming = await api('GET', '/friends/requests', undefined, b.token)
  const request = incoming.json.incoming.find(
    (r: any) => r.requester?.id === a.userId
  )
  if (!request) {
    throw new Error(`incoming friend request from ${a.nickname} not found for ${b.nickname}`)
  }
  const accept = await api(
    'POST',
    `/friends/requests/${request.id}/accept`,
    undefined,
    b.token
  )
  if (accept.status !== 200) {
    throw new Error(`accept failed: ${JSON.stringify(accept.json)}`)
  }
}

// ---------- UI 操作（uni-app H5：自定义元素 uni-input/uni-button + hash 路由） ----------

/**
 * uni-app H5 的 input 不带原生 placeholder 属性（占位符是兄弟 div），按占位符文本
 * 定位 uni-input 容器后填内部真实 input。locator 自动等待元素出现。
 */
export async function fillUniInput(page: Page, placeholder: string, value: string): Promise<void> {
  const wrapper = page.locator('uni-input', { hasText: placeholder }).first()
  await wrapper.locator('input').fill(value)
}

export function uniButton(page: Page, text: string): Locator {
  return page.locator('uni-button', { hasText: text }).first()
}

/**
 * 走 UI 登录并停在首页（ticket 04：新首页，今日卡可见）。
 */
export async function loginViaUi(page: Page, user: TestUser): Promise<void> {
  // 已在登录页（登出后的常态）则不再 goto：hash URL 的 goto 会触发整页 reload，
  // 曾中断进行中的登录 POST（status -1）。仅在需要时跳转。
  if (!(await uniButton(page, '登录').isVisible().catch(() => false))) {
    await page.goto('/#/pages/login/login')
  }
  await expect(uniButton(page, '登录')).toBeVisible({ timeout: 20_000 })
  await fillUniInput(page, '邮箱', user.email)
  await fillUniInput(page, '密码', user.password)
  await uniButton(page, '登录').click()
  await expect(page.locator('.today-card')).toBeVisible({ timeout: 20_000 })
  // uni-app reLaunch 后页面转场/keep-alive 尚在收尾，立刻点入口偶发路由栈错乱
  // （详情见 ticket 15 排查记录）：等转场稳定再继续。
  await page.waitForTimeout(1_500)
}

/**
 * 打开功能入口（发布内容/站内信/好友/搜索…）。ticket 04 删除了旧首页入口列表
 * （部分入口已并入设置页），改为按目标直达路由导航。
 */
export async function openEntry(page: Page, label: string): Promise<void> {
  const entryUrls: Record<string, string> = {
    发布内容: '/pages/publish/publish',
    站内信: '/pages/inbox/inbox',
    好友: '/pages/friends/friends',
    搜索: '/pages/search/search'
  }
  const path = entryUrls[label]
  if (!path) throw new Error(`unknown entry: ${label}`)
  await page.goto(`/#${path}`)
}

/**
 * 进入发布页日志编辑态（视口自适应）：
 * 桌面（≥768px）双栏编辑器常显、移动端默认纯编辑——两者编辑器均直接可见；
 * 仅当编辑器不可见（如 localStorage 记忆停留在预览态）才点标题后切换按钮。
 */
export async function enterJournalEditMode(page: Page): Promise<void> {
  const editor = page.locator('.cherry-editor .cm-content')
  // isVisible 是即时快照（cherry 异步初始化中可能尚未就绪），先给一段可见性等待
  const ready = await editor
    .waitFor({ state: 'visible', timeout: 8_000 })
    .then(() => true)
    .catch(() => false)
  if (ready) return
  await page.locator('.preview-toggle').click()
  await expect(editor).toBeVisible({ timeout: 20_000 })
}

/**
 * 切到日志预览态：桌面双栏预览面板常显（调用为 no-op）；
 * 移动端（<768px）点标题后切换按钮进入满宽预览。验证后用同函数切回。
 */
export async function toggleJournalPreview(page: Page): Promise<void> {
  const isDesktop = await page.evaluate(() => window.matchMedia('(min-width: 768px)').matches)
  if (!isDesktop) await page.locator('.preview-toggle').click()
}

/**
 * 回首页后退出登录。深层页面沿浏览器历史后退（uni-app hash 路由响应 popstate），
 * 避免 hash URL goto 的整页 reload 竞态。退出按钮在桌面侧栏底部（首页不再有退出按钮）。
 */
export async function logoutViaUi(page: Page): Promise<void> {
  for (let i = 0; i < 4; i++) {
    if (page.url().includes('pages/home/home')) break
    await page.goBack()
    await expect(page.locator('.today-card')).toBeVisible({ timeout: 10_000 }).catch(() => {})
  }
  await expect(page.locator('.today-card')).toBeVisible({ timeout: 10_000 })
  await page.locator('.sb-bottom .sb-item', { hasText: '退出登录' }).click()
  await expect(uniButton(page, '登录')).toBeVisible({ timeout: 10_000 })
}
