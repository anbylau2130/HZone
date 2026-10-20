<template>
  <view class="page">
    <view class="profile">
      <AvatarCircle :nickname="auth.user?.nickname ?? ''" :avatar-url="auth.user?.avatarUrl" :size="72" />
      <view class="profile-mid">
        <text class="nick">{{ auth.user?.nickname }}</text>
        <view class="profile-actions">
          <button class="mini" :disabled="busy" @click="pickAvatar">{{ $t('settings.changeAvatar') }}</button>
          <button
            v-if="auth.user?.avatarUrl"
            class="mini ghost"
            :disabled="busy"
            @click="removeAvatar"
          >
            {{ $t('settings.removeAvatar') }}
          </button>
        </view>
        <text class="profile-hint">{{ $t('settings.avatarHint') }}</text>
      </view>
    </view>
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
import { ref } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import AvatarCircle from '../../components/AvatarCircle.vue'
import { uploadAvatar, clearAvatar } from '../../api/endpoints'
import { setNavTitle } from '../../locale'

const { t } = useI18n()
const auth = useAuthStore()
const inbox = useInboxStore()
const busy = ref(false)

onShow(() => {
  inbox.startPolling()
  setNavTitle('settings.title')
})

// H5 走原生 input[type=file]（与发布页 openH5FilePicker 同模式），App 走 uni.chooseImage。
function pickAvatar() {
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) uploadAvatarFile(URL.createObjectURL(file))
  }
  input.click()
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: 1,
    success: (res) => {
      uploadAvatarFile(res.tempFilePaths[0])
    }
  })
  // #endif
}

async function uploadAvatarFile(path: string) {
  busy.value = true
  try {
    await uploadAvatar(path)
    await auth.fetchMe()
    uni.showToast({ title: t('settings.avatarUpdated'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

async function removeAvatar() {
  busy.value = true
  try {
    await clearAvatar()
    await auth.fetchMe()
    uni.showToast({ title: t('settings.avatarRemoved'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

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
.profile {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  align-items: center;
  gap: 16px;
}
.profile-mid {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.nick {
  font-size: var(--hz-font-size-lg, 18px);
  font-weight: 700;
  color: var(--hz-color-text);
}
.profile-actions {
  display: flex;
  gap: 8px;
}
.mini {
  height: 32px;
  line-height: 32px;
  padding: 0 14px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.profile-hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
