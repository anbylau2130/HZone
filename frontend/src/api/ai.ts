import { request } from './client'
import { createJournal, type ContentItem } from './endpoints'

// ---------- AI：Provider / 技能 / MCP / 会话 ----------

export interface AiProviderInfo {
  baseUrl: string
  model: string
  hasKey: boolean
}

export function getAiProvider() {
  return request<AiProviderInfo>('/api/me/ai/provider')
}

export function saveAiProvider(payload: { baseUrl: string; apiKey?: string; model: string }) {
  return request<{ ok: boolean }>('/api/me/ai/provider', { method: 'PUT', data: payload })
}

export function testAiProvider() {
  return request<{ ok: boolean; status?: number; body?: string; error?: string }>(
    '/api/me/ai/provider/test',
    { method: 'POST' }
  )
}

export interface AiSkillInfo {
  id: string
  name: string
  icon: string
  prompt: string
  createdAt: string
}

export function listAiSkills() {
  return request<{ items: AiSkillInfo[] }>('/api/me/ai/skills')
}

export function createAiSkill(payload: { name: string; icon: string; prompt: string }) {
  return request<AiSkillInfo>('/api/me/ai/skills', { method: 'POST', data: payload })
}

export function updateAiSkill(id: string, payload: { name: string; icon: string; prompt: string }) {
  return request<{ id: string }>(`/api/me/ai/skills/${id}`, { method: 'PUT', data: payload })
}

export function deleteAiSkill(id: string) {
  return request<{ ok: boolean }>(`/api/me/ai/skills/${id}`, { method: 'DELETE' })
}

export interface AiMcpServerInfo {
  id: string
  name: string
  url: string
  hasAuth: boolean
  enabled: boolean
  createdAt: string
}

export function listAiMcpServers() {
  return request<{ items: AiMcpServerInfo[] }>('/api/me/ai/mcp-servers')
}

export function createAiMcpServer(payload: { name: string; url: string; authHeader?: string }) {
  return request<AiMcpServerInfo>('/api/me/ai/mcp-servers', { method: 'POST', data: payload })
}

export function updateAiMcpServer(
  id: string,
  payload: { name: string; url: string; authHeader?: string; enabled?: boolean }
) {
  return request<{ id: string }>(`/api/me/ai/mcp-servers/${id}`, { method: 'PUT', data: payload })
}

export function deleteAiMcpServer(id: string) {
  return request<{ ok: boolean }>(`/api/me/ai/mcp-servers/${id}`, { method: 'DELETE' })
}

export function testAiMcpServer(id: string) {
  return request<{ ok: boolean; tools?: { name: string; description: string }[]; error?: string }>(
    `/api/me/ai/mcp-servers/${id}/tools`
  )
}

export interface AiConversationInfo {
  id: string
  title: string
  updatedAt: string
}

export function listAiConversations() {
  return request<{ items: AiConversationInfo[] }>('/api/me/ai/conversations')
}

export function createAiConversation() {
  return request<AiConversationInfo>('/api/me/ai/conversations', { method: 'POST' })
}

export function deleteAiConversation(id: string) {
  return request<{ ok: boolean }>(`/api/me/ai/conversations/${id}`, { method: 'DELETE' })
}

export interface AiMessageInfo {
  id: string
  role: 'user' | 'assistant' | 'tool'
  content: string
  toolCallsJson: string | null
  toolCallId: string | null
  toolName: string | null
  createdAt: string
}

export function listAiMessages(conversationId: string) {
  return request<{ id: string; title: string; items: AiMessageInfo[] }>(
    `/api/me/ai/conversations/${conversationId}/messages`
  )
}

/** SSE 流式对话（delta/tool/done/error 事件回调）。 */
export function streamAiChat(
  conversationId: string,
  content: string,
  skillId: string | null,
  handlers: {
    onDelta: (text: string) => void
    onTool: (payload: { phase: string; name: string; result?: string }) => void
    onDone: () => void
    onError: (msg: string) => void
  }
): { abort: () => void } {
  const controller = new AbortController()
  const token = uni.getStorageSync('hz_token') as string
  void (async () => {
    try {
      const res = await fetch(`/api/me/ai/conversations/${conversationId}/chat`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${token}`,
          'Content-Type': 'application/json'
        },
        body: JSON.stringify({ content, skillId }),
        signal: controller.signal
      })
      if (!res.ok || !res.body) {
        const text = await res.text()
        let msg = text
        try {
          msg = JSON.parse(text).error ?? text
        } catch {
          /* 保留原文 */
        }
        handlers.onError(msg)
        return
      }
      const reader = res.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      for (;;) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        // SSE 帧：空行分隔
        let sep: number
        while ((sep = buffer.indexOf('\n\n')) >= 0) {
          const frame = buffer.slice(0, sep)
          buffer = buffer.slice(sep + 2)
          let event = ''
          let data = ''
          for (const line of frame.split('\n')) {
            if (line.startsWith('event: ')) event = line.slice(7).trim()
            else if (line.startsWith('data: ')) data += line.slice(6)
          }
          if (!event || !data) continue
          const payload = JSON.parse(data)
          if (event === 'delta') handlers.onDelta(payload.text ?? '')
          else if (event === 'tool') handlers.onTool(payload)
          else if (event === 'done') handlers.onDone()
          else if (event === 'error') handlers.onError(payload.error ?? '未知错误')
        }
      }
    } catch (e) {
      if ((e as Error).name !== 'AbortError') handlers.onError((e as Error).message)
    }
  })()
  return { abort: () => controller.abort() }
}

/** 对话存为笔记（前端组 markdown，走现有 createJournal）。 */
export function saveChatAsJournal(title: string, markdown: string): Promise<ContentItem> {
  return createJournal(title, markdown, [])
}
