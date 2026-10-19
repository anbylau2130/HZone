<template>
  <view class="page">
    <view class="card">
      <text class="title">HZone</text>
      <text class="greet">{{ $t('home.welcome', { name: displayName }) }}</text>
      <text v-if="auth.user?.isAdmin" class="badge">{{ $t('home.adminBadge') }}</text>
    </view>
    <view class="entries">
      <view v-for="e in entries" :key="e.key" class="entry" @click="go(e.url)">
        <text class="entry-label">{{ $t(`hub.${e.key}`) }}</text>
        <view v-if="e.key === 'inbox' && inbox.unreadCount > 0" class="dot">
          <text class="dot-text">{{ inbox.unreadCount > 99 ? '99+' : inbox.unreadCount }}</text>
        </view>
      </view>
    </view>
    <button class="btn ghost" @click="logout">{{ $t('home.logout') }}</button>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'

const auth = useAuthStore()
const inbox = useInboxStore()
const displayName = computed(() => auth.user?.nickname ?? '')

const entries = computed(() => {
  const list: { key: string; url: string }[] = [
    { key: 'publish', url: '/pages/publish/publish' },
    { key: 'timeline', url: '/pages/timeline/timeline' },
    { key: 'received', url: '/pages/received/received' },
    { key: 'friends', url: '/pages/friends/friends' },
    { key: 'inbox', url: '/pages/inbox/inbox' },
    { key: 'search', url: '/pages/search/search' },
    { key: 'pats', url: '/pages/pats/pats' },
    { key: 'importExport', url: '/pages/import-export/import-export' }
  ]
  if (auth.user?.isAdmin) {
    list.push({ key: 'admin', url: '/pages/admin/admin' })
  }
  return list
})

onShow(() => {
  if (!auth.token) {
    uni.reLaunch({ url: '/pages/login/login' })
    return
  }
  auth.fetchMe().catch(() => {
    // 401 由请求层统一清会话并跳登录
  })
  inbox.startPolling()
})

onHide(() => {
  inbox.stopPolling()
})

function go(url: string) {
  uni.navigateTo({ url })
}

function logout() {
  inbox.stopPolling()
  auth.logout()
  uni.reLaunch({ url: '/pages/login/login' })
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  box-sizing: border-box;
}
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
}
.title {
  font-size: 24px;
  font-weight: 700;
  color: var(--hz-color-text);
}
.greet {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
}
.badge {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  padding: 2px 10px;
}
.entries {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  overflow: hidden;
}
.entry {
  display: flex;
  align-items: center;
  padding: 15px 16px;
  border-bottom: 1px solid var(--hz-color-border);
}
.entry:last-child {
  border-bottom: none;
}
.entry-label {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.dot {
  margin-left: auto;
  min-width: 18px;
  height: 18px;
  padding: 0 5px;
  border-radius: 9px;
  background: #e54d42;
  display: flex;
  align-items: center;
  justify-content: center;
}
.dot-text {
  font-size: 11px;
  color: #fff;
}
.btn.ghost {
  background: var(--hz-color-surface);
  color: #e54d42;
  border: 1px solid var(--hz-color-border);
}
</style>
