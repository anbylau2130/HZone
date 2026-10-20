<template>
  <view class="page">
    <view class="banner">
      <view class="banner-row">
        <view class="avatar">{{ initial }}</view>
        <view class="banner-mid">
          <text class="nickname">{{ displayName }}</text>
          <text class="signature">{{ auth.user?.email ?? '' }}</text>
        </view>
        <view class="settings-entry" @click="goSettings">
          <text class="settings-gear">⚙</text>
        </view>
      </view>
    </view>
    <view class="big-cards">
      <view class="big-card" @click="goTab('/pages/moments/moments')">
        <text class="big-icon">✎</text>
        <view class="big-mid">
          <text class="big-title">{{ $t('moments.title') }}</text>
          <text class="big-desc">{{ $t('moments.desc') }}</text>
        </view>
        <text class="big-count">{{ sayTotal }}</text>
      </view>
      <view class="big-card" @click="goTab('/pages/journals/journals')">
        <text class="big-icon">▤</text>
        <view class="big-mid">
          <text class="big-title">{{ $t('journals.title') }}</text>
          <text class="big-desc">{{ $t('journals.desc') }}</text>
        </view>
        <text class="big-count">{{ journalTotal }}</text>
      </view>
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
    <AppTabBar current="index" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import { getSetupStatus, listMyContents } from '../../api/endpoints'
import AppTabBar from '../../components/AppTabBar.vue'

const auth = useAuthStore()
const inbox = useInboxStore()
const displayName = computed(() => auth.user?.nickname ?? '')
const initial = computed(() => displayName.value.slice(0, 1))
const sayTotal = ref(0)
const journalTotal = ref(0)

const entries = computed(() => {
  const list: { key: string; url: string }[] = [
    { key: 'publish', url: '/pages/publish/publish' },
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

onShow(async () => {
  try {
    if (!(await getSetupStatus())) {
      uni.reLaunch({ url: '/pages/setup/setup' })
      return
    }
  } catch {
    // 探测失败（后端不可达）不阻断首页
  }
  if (!auth.token) {
    uni.reLaunch({ url: '/pages/login/login' })
    return
  }
  auth.fetchMe().catch(() => {
    // 401 由请求层统一清会话并跳登录
  })
  inbox.startPolling()
  loadCounts()
})

onHide(() => {
  inbox.stopPolling()
})

// 两张大入口卡的内容计数（只取 total，limit=1）
async function loadCounts() {
  try {
    const [say, journal] = await Promise.all([
      listMyContents('say', 0, 1),
      listMyContents('journal', 0, 1)
    ])
    sayTotal.value = say.total
    journalTotal.value = journal.total
  } catch {
    // 计数失败不阻断首页，保持 0
  }
}

function go(url: string) {
  uni.navigateTo({ url })
}

// tab 页之间用 redirectTo（与 AppTabBar 一致，路由栈始终只有一层 tab 页）
function goTab(url: string) {
  uni.redirectTo({ url })
}

function goSettings() {
  uni.navigateTo({ url: '/pages/settings/settings' })
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
  padding: 0 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 16px;
  box-sizing: border-box;
}
.banner {
  margin: 0 -16px;
  padding: 24px 16px 20px;
  background: linear-gradient(180deg, var(--hz-color-primary) 0%, transparent 100%);
  opacity: 0.9;
}
.banner-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.avatar {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  font-size: 24px;
  font-weight: 800;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.nickname {
  font-size: 28px;
  font-weight: 800;
  color: var(--hz-color-text);
}
.signature {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.settings-entry {
  margin-left: auto;
  width: 36px;
  height: 36px;
  border-radius: 50%;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  display: flex;
  align-items: center;
  justify-content: center;
}
.settings-gear {
  font-size: 18px;
  color: var(--hz-color-text-secondary);
  line-height: 1;
}
.big-cards {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 4px;
}
.big-card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 18px 16px;
  display: flex;
  align-items: center;
  gap: 12px;
}
.big-icon {
  font-size: 28px;
  color: var(--hz-color-primary);
  line-height: 1;
}
.big-mid {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
}
.big-title {
  font-size: 18px;
  font-weight: 700;
  color: var(--hz-color-text);
}
.big-desc {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.big-count {
  font-size: 22px;
  font-weight: 700;
  color: var(--hz-color-primary);
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
  background: var(--hz-color-accent);
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
  color: var(--hz-color-accent);
  border: 1px solid var(--hz-color-border);
}
</style>
