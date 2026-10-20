<template>
  <view class="tabbar">
    <view
      v-for="t in tabs"
      :key="t.key"
      class="tab"
      :class="{ on: current === t.key }"
      @click="go(t)"
    >
      <view class="tab-icon-wrap">
        <text class="tab-icon">{{ t.icon }}</text>
        <view v-if="t.key === 'received' && inbox.unreadCount > 0" class="dot">
          <text class="dot-text">{{ inbox.unreadCount > 99 ? '99+' : inbox.unreadCount }}</text>
        </view>
      </view>
      <text class="tab-label">{{ $t(t.labelKey) }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useInboxStore } from '../stores/inbox'

type TabKey = 'index' | 'moments' | 'journals' | 'received' | 'theme' | 'settings'

interface TabItem {
  key: TabKey
  labelKey: string
  icon: string
  url: string
}

const props = defineProps<{ current: TabKey }>()

const tabs: TabItem[] = [
  { key: 'index', labelKey: 'tabs.index', icon: '⌂', url: '/pages/index/index' },
  { key: 'moments', labelKey: 'tabs.moments', icon: '✎', url: '/pages/moments/moments' },
  { key: 'journals', labelKey: 'tabs.journals', icon: '▤', url: '/pages/journals/journals' },
  { key: 'received', labelKey: 'tabs.received', icon: '◉', url: '/pages/received/received' },
  { key: 'theme', labelKey: 'tabs.theme', icon: '◐', url: '/pages/theme/theme' },
  { key: 'settings', labelKey: 'tabs.settings', icon: '⚙', url: '/pages/settings/settings' }
]

const inbox = useInboxStore()
onMounted(() => {
  inbox.startPolling()
})

function go(t: TabItem) {
  if (t.key === props.current) return
  // 自定义 tabBar：tab 页之间用 redirectTo，路由栈里始终只有一层 tab 页
  uni.redirectTo({ url: t.url })
}
</script>

<style scoped>
.tabbar {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  background: var(--hz-color-surface);
  border-top: 1px solid var(--hz-color-border);
  display: flex;
}
.tab {
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 6px 0 calc(6px + env(safe-area-inset-bottom));
  gap: 2px;
}
.tab.on .tab-label,
.tab.on .tab-icon {
  color: var(--hz-color-primary);
}
.tab-label {
  font-size: 10px;
  color: var(--hz-color-text-secondary);
  line-height: 1.2;
}
.tab-icon {
  font-size: 20px;
  color: var(--hz-color-text-secondary);
  line-height: 1.1;
}
.tab-icon-wrap {
  position: relative;
  line-height: 1;
}
.dot {
  position: absolute;
  top: -4px;
  right: -10px;
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
  /* 桌面端用 AppSidebar，底部 tab 栏隐藏 */
  .tabbar {
    display: none;
  }
}
</style>
