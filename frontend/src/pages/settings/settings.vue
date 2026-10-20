<template>
  <view class="page">
    <view class="entries">
      <view class="entry" @click="go('/pages/friends/friends')">
        <text class="entry-label">{{ $t('hub.friends') }}</text>
      </view>
      <view class="entry" @click="go('/pages/pats/pats')">
        <text class="entry-label">{{ $t('hub.pats') }}</text>
      </view>
      <view class="entry" @click="go('/pages/import-export/import-export')">
        <text class="entry-label">{{ $t('hub.importExport') }}</text>
      </view>
      <view v-if="auth.user?.isAdmin" class="entry" @click="go('/pages/admin/admin')">
        <text class="entry-label">{{ $t('hub.admin') }}</text>
      </view>
    </view>
    <button class="btn ghost" @click="logout">{{ $t('home.logout') }}</button>

    <AppSidebar current="settings" />
    <AppTabBar current="settings" />
  </view>
</template>

<script setup lang="ts">
import { onShow, onHide } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import { setNavTitle } from '../../locale'

const auth = useAuthStore()
const inbox = useInboxStore()

onShow(() => {
  inbox.startPolling()
  setNavTitle('settings.title')
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
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: var(--hz-space-lg);
  box-sizing: border-box;
}
.entries {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  overflow: hidden;
}
.entry {
  display: flex;
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
.btn.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-accent);
  border: 1px solid var(--hz-color-border);
}
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding: 24px;
  }
}
</style>
