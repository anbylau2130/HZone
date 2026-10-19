import { ref } from 'vue'
import { defineStore } from 'pinia'
import { request, getToken } from '../api/client'

const POLL_MS = 60_000 // 不打扰原则：60s 轮询未读数，无 websocket

export const useInboxStore = defineStore('inbox', () => inboxState())

function inboxState() {
  const unreadCount = ref(0)

  let timer: ReturnType<typeof setInterval> | null = null

  async function refresh() {
    if (!getToken()) {
      unreadCount.value = 0
      return
    }
    try {
      const res = await request<{ unreadCount: number }>('/api/inbox/unread-count')
      unreadCount.value = res.unreadCount
    } catch {
      // 静默失败：红点晚一分钟更新无妨
    }
  }

  function startPolling() {
    if (timer) return
    refresh()
    timer = setInterval(() => {
      refresh()
    }, POLL_MS)
  }

  function stopPolling() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  function reset() {
    unreadCount.value = 0
  }

  return { unreadCount, refresh, startPolling, stopPolling, reset }
}
