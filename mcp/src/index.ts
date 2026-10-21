#!/usr/bin/env node
/**
 * HZone stdio MCP server。
 *
 * 配置：env HZONE_URL（默认 http://localhost:5000）+ HZONE_PAT（设置页 PAT 管理生成，hz_ 前缀）。
 * 权限=PAT 本人，可见名单规则由后端 IContentQuery 天然生效。
 * 注意：stdio 协议线必须干净——除 MCP JSON-RPC 外禁止任何 stdout 输出。
 */
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js'
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js'
import { z } from 'zod'

const HZONE_URL = (process.env.HZONE_URL ?? 'http://localhost:5000').replace(/\/+$/, '')
const HZONE_PAT = process.env.HZONE_PAT

if (!HZONE_PAT) {
  console.error(
    '[HZone MCP] 缺少环境变量 HZONE_PAT。请到 HZone 设置页「PAT 管理」生成个人令牌（hz_ 开头），' +
      '并在 MCP 客户端配置中设置 HZONE_PAT=<明文令牌>；可选 HZONE_URL 指向 HZone 服务（默认 http://localhost:5000）。'
  )
  process.exit(1)
}

/** 调 HZone REST API。非 2xx 抛带状态码的错；401/网络错给友好中文文案。 */
async function hzoneApi(method: string, path: string, body?: unknown): Promise<unknown> {
  let resp: Response
  try {
    resp = await fetch(HZONE_URL + path, {
      method,
      headers: {
        Authorization: `Bearer ${HZONE_PAT}`,
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })
  } catch {
    throw new Error(`无法连接 HZone（HZONE_URL=${HZONE_URL}），请检查服务是否运行。`)
  }
  if (resp.status === 401) {
    throw new Error('HZone 令牌无效或已吊销，请到设置页重新生成 PAT。')
  }
  if (!resp.ok) {
    const detail = await resp.text().catch(() => '')
    throw new Error(
      `HZone API ${method} ${path} 失败：HTTP ${resp.status}${detail ? `：${detail.slice(0, 300)}` : ''}`
    )
  }
  if (resp.status === 204) return null
  return resp.json()
}

/** 统一工具结果：JSON 文本；handler 抛错时 SDK 自动转 isError 结果。 */
function ok(data: unknown) {
  return { content: [{ type: 'text' as const, text: JSON.stringify(data) }] }
}

const paging = {
  offset: z.coerce.number().int().min(0).default(0).describe('偏移量，默认 0'),
  limit: z.coerce.number().int().min(1).max(100).default(20).describe('每页条数，默认 20，最大 100'),
}
const kindParam = z
  .enum(['say', 'journal'])
  .optional()
  .describe('内容类型：say=随记，journal=长文；不传为全部')

const server = new McpServer({ name: 'hzone', version: '0.1.0' })

// 1. 列我的内容（带 query 时走 /search 全文检索，否则走 /me/contents）
server.registerTool(
  'hzone_list_contents',
  {
    title: '列出我的内容',
    description:
      '列出当前用户的内容（say=随记 / journal=长文）。提供 query 时按可见范围全文检索，否则列出自己发布的内容。',
    inputSchema: {
      kind: kindParam,
      query: z.string().optional().describe('关键词，提供时走全文检索'),
      ...paging,
    },
  },
  async ({ kind, query, offset, limit }) => {
    const common = new URLSearchParams({ offset: String(offset), limit: String(limit) })
    if (kind) common.set('kind', kind)
    const path = query
      ? `/search?${new URLSearchParams({ q: query, ...Object.fromEntries(common) })}`
      : `/me/contents?${common}`
    return ok(await hzoneApi('GET', path))
  }
)

// 2. 内容详情（含评论列表）
server.registerTool(
  'hzone_get_content',
  {
    title: '查看内容详情',
    description: '查看单条内容详情，附带全部评论。',
    inputSchema: { id: z.string().describe('内容 id（GUID）') },
  },
  async ({ id }) => {
    const content = (await hzoneApi('GET', `/contents/${id}`)) as Record<string, unknown>
    const comments = await hzoneApi('GET', `/contents/${id}/comments`)
    return ok({ ...content, comments })
  }
)

// 3. 全文检索
server.registerTool(
  'hzone_search',
  {
    title: '搜索内容',
    description: '在当前用户可见范围内全文检索内容。',
    inputSchema: {
      q: z.string().min(1).describe('搜索关键词'),
      ...paging,
    },
  },
  async ({ q, offset, limit }) => {
    const p = new URLSearchParams({ q, offset: String(offset), limit: String(limit) })
    return ok(await hzoneApi('GET', `/search?${p}`))
  }
)

// 4. 收到的内容
server.registerTool(
  'hzone_list_received',
  {
    title: '收到的内容',
    description: '列出好友分享给我的内容。',
    inputSchema: { kind: kindParam, ...paging },
  },
  async ({ kind, offset, limit }) => {
    const p = new URLSearchParams({ offset: String(offset), limit: String(limit) })
    if (kind) p.set('kind', kind)
    return ok(await hzoneApi('GET', `/me/received?${p}`))
  }
)

// 5. 收件箱摘要
server.registerTool(
  'hzone_inbox',
  {
    title: '收件箱',
    description: '查看收件箱消息（评论、@、好友申请等）与未读数。',
    inputSchema: {},
  },
  async () => ok(await hzoneApi('GET', '/inbox'))
)

// 6. 发随记
server.registerTool(
  'hzone_create_say',
  {
    title: '发随记',
    description: '发布一条随记（say）。recipientIds 指定分享给哪些好友；weather 为可选天气快照。',
    inputSchema: {
      text: z.string().min(1).describe('随记正文'),
      recipientIds: z.array(z.string()).optional().describe('分享给的好友用户 id 列表（GUID）'),
      weather: z
        .object({
          city: z.string(),
          condition: z.string(),
          tempC: z.number(),
        })
        .optional()
        .describe('可选天气快照'),
    },
  },
  async ({ text, recipientIds, weather }) =>
    ok(await hzoneApi('POST', '/contents/says', { text, recipientIds: recipientIds ?? [], weather }))
)

// 7. 发长文
server.registerTool(
  'hzone_create_journal',
  {
    title: '发长文',
    description: '发布一篇长文（journal，Markdown）。recipientIds 指定分享给哪些好友。',
    inputSchema: {
      title: z.string().min(1).describe('标题'),
      markdown: z.string().min(1).describe('Markdown 正文'),
      recipientIds: z.array(z.string()).optional().describe('分享给的好友用户 id 列表（GUID）'),
    },
  },
  async ({ title, markdown, recipientIds }) =>
    ok(await hzoneApi('POST', '/contents/journals', { title, markdown, recipientIds: recipientIds ?? [] }))
)

// 8. 评论
server.registerTool(
  'hzone_comment',
  {
    title: '发表评论',
    description: '对某条内容发表评论。',
    inputSchema: {
      contentId: z.string().describe('内容 id（GUID）'),
      text: z.string().min(1).describe('评论正文'),
    },
  },
  async ({ contentId, text }) => ok(await hzoneApi('POST', `/contents/${contentId}/comments`, { text }))
)

// 9. 好友列表
server.registerTool(
  'hzone_friends',
  {
    title: '好友列表',
    description: '列出当前用户的好友（含 id、昵称、邮箱），id 可用于 recipientIds 分享。',
    inputSchema: {},
  },
  async () => ok(await hzoneApi('GET', '/friends'))
)

await server.connect(new StdioServerTransport())
