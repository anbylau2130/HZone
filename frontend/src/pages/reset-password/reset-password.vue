<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('reset.title') }}</text>
      <input class="field" password v-model="newPassword" :placeholder="$t('reset.newPassword')" placeholder-class="ph" />
      <input class="field" password v-model="confirmPassword" :placeholder="$t('reset.confirmPassword')" placeholder-class="ph" />
      <button class="btn" :disabled="loading" @click="submit">{{ $t('reset.submit') }}</button>
      <view class="links">
        <text class="link" @click="goLogin">{{ $t('forgot.toLogin') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { onLoad } from '@dcloudio/uni-app'
import { useAuthStore } from '../../stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const token = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const loading = ref(false)

onLoad((query) => {
  token.value = String(query?.token ?? '')
})

async function submit() {
  if (loading.value) return
  if (!token.value) {
    uni.showToast({ title: t('reset.missingToken'), icon: 'none' })
    return
  }
  if (newPassword.value !== confirmPassword.value) {
    uni.showToast({ title: t('reset.mismatch'), icon: 'none' })
    return
  }
  loading.value = true
  try {
    await auth.resetPassword(token.value, newPassword.value)
    uni.showToast({ title: t('reset.success'), icon: 'none' })
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
