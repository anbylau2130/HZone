<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('verifyEmail.title') }}</text>
      <text class="status">{{ statusText }}</text>
      <button v-if="state === 'failed'" class="btn retry" @click="verify">{{ $t('verifyEmail.retry') }}</button>
      <button v-else class="btn" @click="goLogin">{{ $t('verifyEmail.toLogin') }}</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const token = ref('')
const state = ref<'verifying' | 'success' | 'failed'>('verifying')

const statusText = computed(() => {
  if (state.value === 'success') return t('verifyEmail.success')
  if (state.value === 'failed') return t('verifyEmail.failed')
  return t('verifyEmail.verifying')
})

onLoad((query) => {
  setNavTitle('verifyEmail.title')
  token.value = String(query?.token ?? '')
  verify()
})

async function verify() {
  state.value = 'verifying'
  try {
    await auth.verifyEmail(token.value)
    state.value = 'success'
  } catch {
    state.value = 'failed'
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
  align-items: center;
}
.title {
  font-size: 28px;
  font-weight: 700;
  color: var(--hz-color-text);
  text-align: center;
}
.status {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  text-align: center;
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
.btn.retry {
  background: var(--hz-color-border);
  color: var(--hz-color-text);
}
</style>
