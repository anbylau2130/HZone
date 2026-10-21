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
    <!-- 个人资料区（moments-wechat 票 02）：签名编辑 + 封面上传/移除 -->
    <view class="profile-card">
      <text class="sec-title">{{ $t('settings.profileSection') }}</text>
      <view class="form-row">
        <text class="form-label">{{ $t('settings.signature') }}</text>
        <input
          v-model="signature"
          class="field"
          type="text"
          :maxlength="100"
          :placeholder="$t('settings.signaturePlaceholder')"
          placeholder-class="ph"
        />
        <button class="mini" :disabled="busy" @click="saveSignature">
          {{ $t('common.save') }}
        </button>
      </view>
      <view class="form-row">
        <text class="form-label">{{ $t('settings.cover') }}</text>
        <view class="profile-actions">
          <button class="mini" :disabled="busy" @click="pickCover">{{ $t('settings.uploadCover') }}</button>
          <button v-if="auth.user?.coverUrl" class="mini ghost" :disabled="busy" @click="removeCover">
            {{ $t('settings.removeCover') }}
          </button>
        </view>
      </view>
      <text class="profile-hint">{{ $t('settings.coverHint') }}</text>
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
import { uploadAvatar, clearAvatar, updateMyProfile, uploadCover, clearCover } from '../../api/endpoints'
import { setNavTitle } from '../../locale'

const { t } = useI18n()
const auth = useAuthStore()
const inbox = useInboxStore()
const busy = ref(false)
// 签名草稿：进入页面时从 store 回填
const signature = ref(auth.user?.signature ?? '')

onShow(() => {
  inbox.startPolling()
  setNavTitle('settings.title')
  // 同步最新资料（头像/签名/封面可能在他页改动）
  signature.value = auth.user?.signature ?? ''
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

// 保存签名：整体替换，空串视为清空
async function saveSignature() {
  busy.value = true
  try {
    await updateMyProfile(signature.value.trim() || null)
    await auth.fetchMe()
    uni.showToast({ title: t('common.saved'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

// 封面上传/移除：与头像同一 H5 input[type=file] 模式
function pickCover() {
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) uploadCoverFile(URL.createObjectURL(file))
  }
  input.click()
  // #endif
  // #ifndef H5
  uni.chooseImage({
    count: 1,
    success: (res) => {
      uploadCoverFile(res.tempFilePaths[0])
    }
  })
  // #endif
}

async function uploadCoverFile(path: string) {
  busy.value = true
  try {
    await uploadCover(path)
    await auth.fetchMe()
    uni.showToast({ title: t('settings.coverUpdated'), icon: 'success' })
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    busy.value = false
  }
}

async function removeCover() {
  busy.value = true
  try {
    await clearCover()
    await auth.fetchMe()
    uni.showToast({ title: t('settings.coverRemoved'), icon: 'success' })
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
.profile-card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.sec-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.form-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
.form-label {
  width: 48px;
  flex-shrink: 0;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.field {
  flex: 1;
  height: 38px;
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
