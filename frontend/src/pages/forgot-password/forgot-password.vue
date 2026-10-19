<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('forgot.title') }}</text>
      <input class="field" type="text" v-model="email" :placeholder="$t('fields.email')" placeholder-class="ph" />
      <button class="btn" :disabled="loading" @click="submit">{{ $t('forgot.submit') }}</button>
      <view class="links">
        <text class="link" @click="goLogin">{{ $t('forgot.toLogin') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/auth'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

onShow(() => {
  setNavTitle('forgot.title')
})

const { t } = useI18n()
const auth = useAuthStore()
const email = ref('')
const loading = ref(false)

async function submit() {
  if (loading.value) return
  loading.value = true
  try {
    await auth.forgotPassword(email.value)
    uni.showToast({ title: t('forgot.success'), icon: 'none' })
    setTimeout(() => uni.reLaunch({ url: '/pages/login/login' }), 1200)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    loading.value = false
  }
}

function goLogin() {
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
}
.title {
  font-size: 28px;
  font-weight: 700;
  color: var(--hz-color-text);
  text-align: center;
}
.field {
  height: 44px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.ph {
  color: var(--hz-color-text-secondary);
}
.btn {
  height: 44px;
  line-height: 44px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
.link {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
</style>
