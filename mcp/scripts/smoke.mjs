/**
 * MCP smoke 测试：spawn dist/index.js，走 stdio JSON-RPC 全链路。
 * 前置：HZone 服务跑在 HZONE_URL（默认 :5000），环境变量 HZONE_PAT 为有效令牌。
 * 用法：HZONE_PAT=hz_xxx node scripts/smoke.mjs
 */
import { spawn } from 'node:child_process'
import { createInterface } from 'node:readline'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const HZONE_URL = process.env.HZONE_URL ?? 'http://localhost:5000'
if (!process.env.HZONE_PAT) {
  console.error('缺少 HZONE_PAT 环境变量')
  process.exit(1)
}

const child = spawn('node', [path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'dist', 'index.js')], {
  env: { ...process.env, HZONE_URL },
  stdio: ['pipe', 'pipe', 'inherit'],
})

const pending = new Map()
let nextId = 1

const rl = createInterface({ input: child.stdout })
rl.on('line', line => {
  line = line.trim()
  if (!line) return
  let msg
  try {
    msg = JSON.parse(line)
  } catch {
    fail(`非 JSON 协议线出现在 stdout（stdio 线必须干净）: ${line.slice(0, 120)}`)
    return
  }
  if (msg.id !== undefined && pending.has(msg.id)) {
    pending.get(msg.id)(msg)
    pending.delete(msg.id)
  }
})

let exited = false
child.on('exit', code => {
  exited = true
  if (code !== 0) fail(`server 进程异常退出，code=${code}`)
})

function fail(message) {
  console.error(`SMOKE FAIL: ${message}`)
  child.kill()
  process.exit(1)
}

function request(method, params, { expectError = false } = {}) {
  const id = nextId++
  const payload = { jsonrpc: '2.0', id, method, ...(params !== undefined ? { params } : {}) }
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`等待 ${method} 响应超时`)), 10000)
    pending.set(id, msg => {
      clearTimeout(timer)
      if (!expectError && msg.error) return reject(new Error(`${method} 返回 error: ${JSON.stringify(msg.error)}`))
      resolve(msg)
    })
    child.stdin.write(JSON.stringify(payload) + '\n')
  })
}

function assert(cond, message) {
  if (!cond) fail(message)
}

function toolText(result) {
  assert(Array.isArray(result?.content) && result.content.length > 0, `tools/call 结果缺 content: ${JSON.stringify(result)}`)
  return result.content.map(c => c.text ?? '').join('')
}

try {
  // 1. initialize
  const init = await request('initialize', {
    protocolVersion: '2024-11-05',
    capabilities: {},
    clientInfo: { name: 'hzone-smoke', version: '0.1.0' },
  })
  assert(init.result?.serverInfo?.name === 'hzone', `initialize 响应异常: ${JSON.stringify(init)}`)

  // 2. initialized 通知（无 id，无响应）
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n')

  // 3. tools/list：断言 9 个 hzone_ 工具
  const tools = await request('tools/list', {})
  const names = (tools.result?.tools ?? []).map(t => t.name)
  const expected = [
    'hzone_list_contents',
    'hzone_get_content',
    'hzone_search',
    'hzone_list_received',
    'hzone_inbox',
    'hzone_create_say',
    'hzone_create_journal',
    'hzone_comment',
    'hzone_friends',
  ]
  for (const name of expected) {
    assert(names.includes(name), `tools/list 缺少工具 ${name}（实际: ${names.join(', ')}）`)
  }
  console.log(`OK tools/list: ${names.length} 个工具 ${names.join(', ')}`)

  // 4. hzone_inbox：断言 unreadCount 存在
  const inbox = await request('tools/call', { name: 'hzone_inbox', arguments: {} })
  assert(!inbox.result?.isError, `hzone_inbox 报错: ${toolText(inbox.result)}`)
  const inboxData = JSON.parse(toolText(inbox.result))
  assert(typeof inboxData.unreadCount === 'number', `inbox 响应缺 unreadCount: ${JSON.stringify(inboxData).slice(0, 300)}`)
  console.log(`OK hzone_inbox: unreadCount=${inboxData.unreadCount}, total=${inboxData.total}`)

  // 5. hzone_create_say：真实写入，断言返回内容 id
  const create = await request('tools/call', {
    name: 'hzone_create_say',
    arguments: { text: 'MCP smoke 测试' },
  })
  assert(!create.result?.isError, `hzone_create_say 报错: ${toolText(create.result)}`)
  const created = JSON.parse(toolText(create.result))
  assert(typeof created.id === 'string' && created.id.length > 0, `create 响应缺 id: ${JSON.stringify(created).slice(0, 300)}`)
  assert(created.kind === 'say', `create 响应 kind 异常: ${created.kind}`)
  console.log(`OK hzone_create_say: id=${created.id}`)

  console.log('SMOKE PASS')
  child.kill()
  process.exit(0)
} catch (err) {
  fail(err?.message ?? String(err))
}
