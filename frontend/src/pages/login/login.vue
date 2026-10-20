<template>
  <view class="page">
    <view class="card">
      <text class="title">HZone</text>
      <text class="sub">{{ $t('app.tagline') }}</text>
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
      <button class="btn" :disabled="loading" @click="submit">
        {{ loading ? $t('common.loading') : $t('login.submit') }}
      </button>
      <view class="links">
        <text class="link" @click="goForgot">{{ $t('login.toForgot') }}</text>
        <text class="link" @click="goRegister">{{ $t('login.toRegister') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useAuthStore } from '../../stores/auth'
import { getSetupStatus } from '../../api/endpoints'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

onShow(async () => {
  setNavTitle('login.title')
  try {
    if (!(await getSetupStatus())) {
      uni.reLaunch({ url: '/pages/setup/setup' })
    }
  } catch {
    // 探测失败（后端不可达）不阻断登录页
  }
})

const auth = useAuthStore()
const email = ref('')
const password = ref('')
const loading = ref(false)

async function submit() {
  if (loading.value) return
  loading.value = true
  try {
    await auth.login(email.value, password.value)
    uni.reLaunch({ url: '/pages/home/home' })
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    loading.value = false
  }
}

function goForgot() {
  uni.navigateTo({ url: '/pages/forgot-password/forgot-password' })
}

function goRegister() {
  uni.navigateTo({ url: '/pages/register/register' })
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
</style>
