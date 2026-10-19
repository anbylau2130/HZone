<template>
  <view class="page">
    <view class="card">
      <text class="title">HZone</text>
      <text class="greet">{{ $t('home.welcome', { name: displayName }) }}</text>
      <text v-if="auth.user?.isAdmin" class="badge">{{ $t('home.adminBadge') }}</text>
      <button class="btn" @click="logout">{{ $t('home.logout') }}</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'

const auth = useAuthStore()
const displayName = computed(() => auth.user?.nickname ?? '')

onShow(() => {
  if (!auth.token) {
    uni.reLaunch({ url: '/pages/login/login' })
    return
  }
  auth.fetchMe().catch(() => {
    // 401 由请求层统一清会话并跳登录
  })
})

function logout() {
  auth.logout()
  uni.reLaunch({ url: '/pages/login/login' })
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  display: flex;
  align-items: center;
  justify-content: center;
}
.card {
  width: 86%;
  max-width: 400px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 32px 24px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: center;
}
.title {
  font-size: 28px;
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
.btn {
  width: 100%;
  height: 44px;
  line-height: 44px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
</style>
