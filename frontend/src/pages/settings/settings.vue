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
    <!-- 自主题页迁入（tab 合并）：外观模式 / 预置主题 / 自定义配色 / 界面语言 -->
    <view class="profile-card">
      <text class="sec-title">{{ $t('theme.mode') }}</text>
      <view class="seg">
        <view class="seg-item" :class="{ active: theme.mode === 'light' }" @click="pickMode('light')">
          <text class="seg-text" :class="{ active: theme.mode === 'light' }">{{ $t('theme.modeLight') }}</text>
        </view>
        <view class="seg-item" :class="{ active: theme.mode === 'dark' }" @click="pickMode('dark')">
          <text class="seg-text" :class="{ active: theme.mode === 'dark' }">{{ $t('theme.modeDark') }}</text>
        </view>
      </view>
    </view>

    <view class="profile-card">
      <text class="sec-title">{{ $t('theme.presets') }}</text>
      <view class="preset-grid">
        <view
          v-for="p in presets"
          :key="p.id"
          class="preset"
          :class="{ selected: theme.presetId === p.id }"
          @click="pickPreset(p.id)"
        >
          <view class="swatch" :style="{ background: p.tokens.bg }">
            <view class="swatch-bar" :style="{ background: p.tokens.primary }" />
            <view class="swatch-line" :style="{ background: p.tokens.text }" />
          </view>
          <text class="preset-name">{{ $t(p.nameKey) }}</text>
        </view>
      </view>
    </view>

    <view class="profile-card">
      <text class="sec-title">{{ $t('theme.custom') }}</text>
      <!-- #ifdef H5 -->
      <text class="hint">{{ $t('theme.customHint') }}</text>
      <view v-for="f in colorFields" :key="f.key" class="color-row">
        <text class="color-label">{{ $t(f.labelKey) }}</text>
        <NativeColorInput v-model="draft[f.key]" />
      </view>
      <view v-if="issues.text" class="warn">
        <text class="warn-text">{{ $t('theme.contrastTextFail') }}</text>
      </view>
      <view v-if="issues.primary" class="warn">
        <text class="warn-text">{{ $t('theme.contrastPrimaryFail') }}</text>
      </view>
      <view class="btn-row">
        <button class="theme-btn primary" :disabled="hasIssues" :class="{ disabled: hasIssues }" @click="saveCustom">{{ $t('common.save') }}</button>
        <button class="theme-btn ghost" @click="resetCustom">{{ $t('theme.resetDefault') }}</button>
      </view>
      <!-- #endif -->
      <!-- #ifndef H5 -->
      <text class="hint">{{ $t('theme.customH5Only') }}</text>
      <!-- #endif -->
    </view>

    <view class="profile-card">
      <text class="sec-title">{{ $t('theme.language') }}</text>
      <view class="seg">
        <view class="seg-item" :class="{ active: locale === 'zh-CN' }" @click="pickLocale('zh-CN')">
          <text class="seg-text" :class="{ active: locale === 'zh-CN' }">中文</text>
        </view>
        <view class="seg-item" :class="{ active: locale === 'en-US' }" @click="pickLocale('en-US')">
          <text class="seg-text" :class="{ active: locale === 'en-US' }">English</text>
        </view>
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
import { ref, computed, reactive, watch } from 'vue'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../stores/auth'
import { useInboxStore } from '../../stores/inbox'
import { useThemeStore } from '../../stores/theme'
import { presets } from '../../theme/presets'
import { checkContrast, TEXT_CONTRAST_MIN, PRIMARY_CONTRAST_MIN } from '../../utils/contrast'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import AvatarCircle from '../../components/AvatarCircle.vue'
import NativeColorInput from '../../components/NativeColorInput.vue'
import { uploadAvatar, clearAvatar, updateMyProfile, uploadCover, clearCover } from '../../api/endpoints'
import { setLocale, setNavTitle, type AppLocale } from '../../locale'

const { t, locale } = useI18n()
const auth = useAuthStore()
const inbox = useInboxStore()
const theme = useThemeStore()
const busy = ref(false)
// 签名草稿：进入页面时从 store 回填
const signature = ref(auth.user?.signature ?? '')

// 自主题页迁入（tab 合并）：自定义配色草稿与对比度校验状态
const draft = reactive({ primary: '', bg: '', surface: '', text: '' })
const issues = reactive({ text: false, primary: false })

const colorFields = [
  { key: 'primary' as const, labelKey: 'theme.colorPrimary' },
  { key: 'bg' as const, labelKey: 'theme.colorBg' },
  { key: 'surface' as const, labelKey: 'theme.colorSurface' },
  { key: 'text' as const, labelKey: 'theme.colorText' }
]

const hasIssues = computed(() => issues.text || issues.primary)

function syncDraft() {
  draft.primary = theme.colors.primary
  draft.bg = theme.colors.bg
  draft.surface = theme.colors.surface
  draft.text = theme.colors.text
}

// 颜色调整：实时预览（仅通过校验时应用），问题红字提示
watch(draft, updateDraft)

function updateDraft() {
  const check = checkContrast(draft)
  issues.text = check.textOnSurface < TEXT_CONTRAST_MIN
  issues.primary = check.primaryOnSurface < PRIMARY_CONTRAST_MIN
  if (check.ok) {
    theme.preview({
      ...theme.colors,
      primary: draft.primary,
      bg: draft.bg,
      surface: draft.surface,
      text: draft.text
    })
  }
}

function saveCustom() {
  const check = checkContrast(draft)
  if (!check.ok) {
    uni.showToast({ title: t('theme.contrastFailToast'), icon: 'none' })
    return
  }
  theme.setOverrides({ primary: draft.primary, bg: draft.bg, surface: draft.surface, text: draft.text })
  uni.showToast({ title: t('common.saved'), icon: 'none' })
}

function pickMode(m: 'light' | 'dark') {
  theme.setMode(m)
}

function pickPreset(id: string) {
  theme.setPreset(id)
  syncDraft()
}

function resetCustom() {
  theme.reset()
  syncDraft()
}

function pickLocale(l: AppLocale) {
  setLocale(l)
  setNavTitle('settings.title')
}

onShow(() => {
  inbox.startPolling()
  setNavTitle('settings.title')
  // 同步最新资料（头像/签名/封面可能在他页改动）
  signature.value = auth.user?.signature ?? ''
  // 自主题页迁入：回填配色草稿
  syncDraft()
})

// H5 走原生 input[type=file]（与发布页 openH5FilePicker 同模式），App 走 uni.chooseImage。
function pickAvatar() {
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'image/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) uploadAvatarFile(file)
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

async function uploadAvatarFile(path: string | Blob) {
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
    if (file) uploadCoverFile(file)
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

async function uploadCoverFile(path: string | Blob) {
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
  // 自主题页迁入：未保存的预览在离开页面时回退为已持久化主题
  theme.apply()
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
/* 自主题页迁入（tab 合并）：以下为主题/语言区块样式（按钮类改名 theme-btn，避免与上方 .btn.ghost 冲突） */
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.color-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  height: 40px;
}
.color-label {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.warn {
  background: rgba(229, 77, 66, 0.08);
  border-radius: var(--hz-radius-sm);
  padding: 8px 10px;
}
.warn-text {
  font-size: var(--hz-font-size-sm);
  color: #e54d42;
}
.preset-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}
.preset {
  width: 104px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  padding: 10px 6px;
  border: 2px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  box-sizing: border-box;
}
.preset.selected {
  border-color: var(--hz-color-primary);
}
.swatch {
  width: 80px;
  height: 48px;
  border-radius: 8px;
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-sizing: border-box;
}
.swatch-bar {
  width: 36px;
  height: 10px;
  border-radius: 4px;
}
.swatch-line {
  width: 56px;
  height: 6px;
  border-radius: 3px;
}
.preset-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.seg {
  display: flex;
  gap: 8px;
}
.seg-item {
  flex: 1;
  height: 38px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
}
.seg-item.active {
  border-color: var(--hz-color-primary);
  background: var(--hz-color-primary);
}
.seg-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.seg-text.active {
  color: #fff;
}
.btn-row {
  display: flex;
  gap: 8px;
}
.theme-btn {
  flex: 1;
  height: 40px;
  line-height: 40px;
  font-size: var(--hz-font-size-sm);
}
.theme-btn.primary {
  background: var(--hz-color-primary);
  color: #fff;
}
.theme-btn.primary.disabled {
  opacity: 0.5;
}
.theme-btn.ghost {
  background: transparent;
  color: var(--hz-color-text);
  border: 1px solid var(--hz-color-border);
}
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding: 24px;
  }
}
</style>
