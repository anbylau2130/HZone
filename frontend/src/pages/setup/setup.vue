<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('setup.title') }}</text>
      <text class="sub">{{ $t('setup.subtitle') }}</text>
      <input
        class="field"
        type="text"
        v-model="email"
        :placeholder="$t('fields.email')"
        placeholder-class="ph"
      />
      <input
        class="field"
        password
        v-model="password"
        :placeholder="$t('fields.password')"
        placeholder-class="ph"
      />
      <input
        class="field"
        password
        v-model="confirmPassword"
        :placeholder="$t('setup.confirmPassword')"
        placeholder-class="ph"
      />
      <input
        class="field"
        type="text"
        v-model="nickname"
        :placeholder="$t('fields.nickname')"
        placeholder-class="ph"
      />
      <button class="btn" :disabled="loading" @click="submit">
        {{ loading ? $t('common.loading') : $t('setup.submit') }}
      </button>
      <view class="links">
        <text class="link" @click="goLogin">{{ $t('setup.toLogin') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { useAuthStore } from '../../stores/auth'
import { getSetupStatus, markSetupInitialized, createAdmin } from '../../api/endpoints'
import type { ApiError } from '../../api/client'

const { t } = useI18n()
const auth = useAuthStore()
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const nickname = ref('')
const loading = ref(false)

onLoad(() => {
  setNavTitle('setup.title')
  getSetupStatus()
    .then((initialized) => {
      if (initialized) uni.reLaunch({ url: '/pages/index/index' })
    })
    .catch(() => {
      // 探测失败（后端不可达）：停留本页，提交时会再报错
    })
})

async function submit() {
  if (loading.value) return
  const mail = email.value.trim()
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(mail)) {
    uni.showToast({ title: t('setup.invalidEmail'), icon: 'none' })
    return
  }
  if (password.value.length < 8) {
    uni.showToast({ title: t('setup.passwordTooShort'), icon: 'none' })
    return
  }
  if (password.value !== confirmPassword.value) {
    uni.showToast({ title: t('setup.passwordMismatch'), icon: 'none' })
    return
  }
  if (!nickname.value.trim()) {
    uni.showToast({ title: t('setup.nicknameRequired'), icon: 'none' })
    return
  }
  loading.value = true
  try {
    await createAdmin(mail, password.value, nickname.value.trim())
    markSetupInitialized()
    await auth.login(mail, password.value)
    uni.showToast({ title: t('setup.success'), icon: 'success' })
    setTimeout(() => uni.reLaunch({ url: '/pages/index/index' }), 800)
    return
  } catch (e) {
    const err = e as ApiError
    if (err?.status === 409) {
      uni.showToast({ title: t('setup.alreadyInitialized'), icon: 'none' })
      setTimeout(() => uni.reLaunch({ url: '/pages/login/login' }), 1200)
    }
    // 其他错误由请求层统一 toast
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
  align-items: stretch;
}
.title {
  font-size: 28px;
  font-weight: 700;
  color: var(--hz-color-text);
  text-align: center;
}
.sub {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
.links {
  display: flex;
  justify-content: center;
}
</style>
