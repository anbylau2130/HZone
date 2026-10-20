import { test, expect } from '@playwright/test'
import {
  ensureInstanceInitialized,
  registerAndVerifyViaApi,
  makeFriends,
  API_BASE
} from './helpers'

// ticket 06：那年今天 / 互动摘要 / 月度回顾三端点的正确性（HTTP 层 e2e，复用 journey 的注册/好友链路）。
// UI 展示层沿用 journey 7 的首页自检（webServer 拉起后，首页模块由 stats/otd 加载静默渲染）。
test.describe.configure({ mode: 'serial' })

const YEAR = new Date().getFullYear()

async function get(path: string, token: string) {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { Authorization: `Bearer ${token}` }
  })
  return { status: res.status, json: await res.json() }
}

async function post(path: string, token: string, body: unknown) {
  const res = await fetch(`${API_BASE}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(body)
  })
  return { status: res.status, json: await res.json() }
}

test.beforeAll(() => ensureInstanceInitialized())

test('06 on-this-day: this year excluded (past-year semantics covered by dotnet tests)', async () => {
  const user = await registerAndVerifyViaApi('OtdE2e')
  const say = await post('/contents/says', user.token, {
    text: '06 e2e 今年今天不匹配',
    recipientIds: []
  })
  expect(say.status).toBe(201)
  const list = await get('/me/on-this-day', user.token)
  expect(list.status).toBe(200)
  expect(Array.isArray(list.json)).toBe(true)
  // MM-dd 匹配仅限往年：今年今天发布的内容不出现。
  expect(list.json.some((i: { id: string }) => i.id === say.json.id)).toBe(false)
  expect(list.json.every((i: { year: number }) => i.year < YEAR)).toBe(true)
})

test('06 interactions summary: comments on my content + mentions within 7d', async () => {
  const a = await registerAndVerifyViaApi('IntE2eA')
  const b = await registerAndVerifyViaApi('IntE2eB')
  await makeFriends(a, b)

  // a 发布给 b（收件人 → Mentioned 站内信），b 评论 a 的内容。
  const say = await post('/contents/says', a.token, {
    text: '06 e2e 互动摘要',
    recipientIds: [b.userId]
  })
  expect(say.status).toBe(201)
  const comment = await post(`/contents/${say.json.id}/comments`, b.token, {
    text: '06 e2e 评论'
  })
  expect(comment.status).toBe(201)

  // a：我内容收到 1 条评论、没被 @；b：被 @ 1 次、我内容 0 评论。
  const sumA = await get('/me/interactions/summary', a.token)
  expect(sumA.status).toBe(200)
  expect(sumA.json).toEqual({ comments: 1, mentions: 0 })
  const sumB = await get('/me/interactions/summary', b.token)
  expect(sumB.json).toEqual({ comments: 0, mentions: 1 })
})

test('06 stats monthly: current-month says/journals/cities/topCondition', async () => {
  const u = await registerAndVerifyViaApi('MonE2e')
  const say = await post('/contents/says', u.token, {
    text: '06 e2e 月度随记',
    recipientIds: [],
    weather: { city: '杭州', condition: 'sunny', tempC: 20 }
  })
  expect(say.status).toBe(201)
  const journal = await post('/contents/journals', u.token, {
    title: '06 e2e 月度日志',
    markdown: '正文',
    recipientIds: [],
    weather: { city: '上海', condition: 'rain', tempC: 18 }
  })
  expect(journal.status).toBe(201)

  const stats = await get('/me/stats', u.token)
  expect(stats.status).toBe(200)
  const monthly = stats.json.monthly
  expect(monthly.says).toBe(1)
  expect(monthly.journals).toBe(1)
  expect(monthly.cities).toBe(2)
  expect(monthly.topCondition).toBeTruthy()
})
