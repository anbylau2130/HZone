<template>
  <!-- 桌面专属（≥768px）左侧边栏；移动端隐藏，移动端仍用 AppTabBar -->
  <view class="sidebar">
    <view class="brand">
      <text class="brand-text">HZone</text>
    </view>
    <view class="sb-user" @click="goTimeline">
      <AvatarCircle :nickname="auth.user?.nickname ?? ''" :avatar-url="auth.user?.avatarUrl" :size="34" />
      <text class="sb-nick">{{ auth.user?.nickname ?? '' }}</text>
      <view class="sb-spacer" />
    </view>
    <view class="nav">
      <view
        v-for="t in tabs"
        :key="t.key"
        class="sb-item"
        :class="{ on: current === t.key }"
        @click="go(t)"
      >
        <text class="sb-icon">{{ t.icon }}</text>
        <text class="sb-label">{{ $t(t.labelKey) }}</text>
        <view v-if="t.key === 'received' && inbox.unreadCount > 0" class="dot">
          <text class="dot-text">{{ inbox.unreadCount > 99 ? '99+' : inbox.unreadCount }}</text>
        </view>
      </view>
    </view>
    <view class="sb-bottom">
      <view class="sb-item" @click="logout">
        <text class="sb-icon">↩</text>
        <text class="sb-label">{{ $t('home.logout') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useInboxStore } from '../stores/inbox'
import { useAuthStore } from '../stores/auth'
import AvatarCircle from './AvatarCircle.vue'

type TabKey = 'index' | 'moments' | 'journals' | 'received' | 'settings'

interface TabItem {
  key: TabKey
  labelKey: string
  icon: string
  url: string
}

const props = defineProps<{ current: TabKey }>()

const tabs: TabItem[] = [
  { key: 'index', labelKey: 'tabs.index', icon: '⌂', url: '/pages/home/home' },
  { key: 'moments', labelKey: 'tabs.moments', icon: '✎', url: '/pages/moments/moments' },
  { key: 'journals', labelKey: 'tabs.journals', icon: '▤', url: '/pages/journals/journals' },
  { key: 'received', labelKey: 'tabs.received', icon: '◉', url: '/pages/received/received' },
  { key: 'settings', labelKey: 'tabs.settings', icon: '⚙', url: '/pages/settings/settings' }
]

const inbox = useInboxStore()
const auth = useAuthStore()
onMounted(() => {
  inbox.startPolling()
})

function go(t: TabItem) {
  if (t.key === props.current) return
  // 与 AppTabBar 一致：tab 页之间用 redirectTo，路由栈始终只有一层 tab 页
  uni.redirectTo({ url: t.url })
}

function goTimeline() {
  uni.navigateTo({ url: '/pages/timeline/timeline' })
}

function logout() {
  inbox.stopPolling()
  auth.logout()
  uni.reLaunch({ url: '/pages/login/login' })
}
</script>

<style scoped>
.sidebar {
  /* 移动端隐藏 */
  display: none;
}
@media (min-width: 768px) {
  .sidebar {
    display: flex;
    flex-direction: column;
    position: fixed;
    top: 0;
    bottom: 0;
    left: 0;
    width: 220px;
    background: var(--hz-color-surface);
    border-right: 1px solid var(--hz-color-border);
    padding: 20px 12px 16px;
    gap: 16px;
    box-sizing: border-box;
    z-index: 100;
  }
}
.brand-text {
  font-size: 20px;
  font-weight: 800;
  color: var(--hz-color-primary);
  padding: 0 12px;
}
.sb-user {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.sb-nick {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.nav {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.sb-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: var(--hz-radius-md);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
.sb-item.on {
  color: var(--hz-color-primary);
  background: var(--hz-color-bg);
  font-weight: 600;
}
.sb-icon {
  font-size: 18px;
  line-height: 1.1;
}
.sb-label {
  font-size: var(--hz-font-size-base);
}
.sb-spacer {
  flex: 1;
}
.dot {
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  background: var(--hz-color-accent);
  display: flex;
  align-items: center;
  justify-content: center;
}
.dot-text {
  font-size: 10px;
  color: #fff;
}
@media (min-width: 768px) {
  .sb-bottom {
    display: flex;
    flex-direction: column;
    gap: 4px;
    margin-top: auto;
  }
  .sb-item.on {
    /* 底部动作项不参与 tab 高亮 */
  }
}
</style>
