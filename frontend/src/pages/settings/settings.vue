<template>
  <view class="page">
    <view class="card">
      <text class="section-title">{{ $t('settings.mode') }}</text>
      <view class="seg">
        <view class="seg-item" :class="{ active: theme.mode === 'light' }" @click="pickMode('light')">
          <text class="seg-text" :class="{ active: theme.mode === 'light' }">{{ $t('settings.modeLight') }}</text>
        </view>
        <view class="seg-item" :class="{ active: theme.mode === 'dark' }" @click="pickMode('dark')">
          <text class="seg-text" :class="{ active: theme.mode === 'dark' }">{{ $t('settings.modeDark') }}</text>
        </view>
      </view>
    </view>

    <view class="card">
      <text class="section-title">{{ $t('settings.presets') }}</text>
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

    <view class="card">
      <text class="section-title">{{ $t('settings.custom') }}</text>
      <!-- #ifdef H5 -->
      <text class="hint">{{ $t('settings.customHint') }}</text>
      <view v-for="f in colorFields" :key="f.key" class="color-row">
        <text class="color-label">{{ $t(f.labelKey) }}</text>
        <NativeColorInput v-model="draft[f.key]" />
      </view>
      <view v-if="issues.text" class="warn">
        <text class="warn-text">{{ $t('settings.contrastTextFail') }}</text>
      </view>
      <view v-if="issues.primary" class="warn">
        <text class="warn-text">{{ $t('settings.contrastPrimaryFail') }}</text>
      </view>
      <view class="btn-row">
        <button class="btn primary" :disabled="hasIssues" :class="{ disabled: hasIssues }" @click="saveCustom">{{ $t('common.save') }}</button>
        <button class="btn ghost" @click="resetCustom">{{ $t('settings.resetDefault') }}</button>
      </view>
      <!-- #endif -->
      <!-- #ifndef H5 -->
      <text class="hint">{{ $t('settings.customH5Only') }}</text>
      <!-- #endif -->
    </view>

    <view class="card">
      <text class="section-title">{{ $t('settings.language') }}</text>
      <view class="seg">
        <view class="seg-item" :class="{ active: locale === 'zh-CN' }" @click="pickLocale('zh-CN')">
          <text class="seg-text" :class="{ active: locale === 'zh-CN' }">中文</text>
        </view>
        <view class="seg-item" :class="{ active: locale === 'en-US' }" @click="pickLocale('en-US')">
          <text class="seg-text" :class="{ active: locale === 'en-US' }">English</text>
        </view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onShow, onHide } from '@dcloudio/uni-app'
import { useThemeStore } from '../../stores/theme'
import { presets } from '../../theme/presets'
import { checkContrast, TEXT_CONTRAST_MIN, PRIMARY_CONTRAST_MIN } from '../../utils/contrast'
import NativeColorInput from '../../components/NativeColorInput.vue'
import { setLocale, setNavTitle, type AppLocale } from '../../locale'

const { t, locale } = useI18n()
const theme = useThemeStore()

const draft = reactive({ primary: '', bg: '', surface: '', text: '' })
const issues = reactive({ text: false, primary: false })

const colorFields = [
  { key: 'primary' as const, labelKey: 'settings.colorPrimary' },
  { key: 'bg' as const, labelKey: 'settings.colorBg' },
  { key: 'surface' as const, labelKey: 'settings.colorSurface' },
  { key: 'text' as const, labelKey: 'settings.colorText' }
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
    uni.showToast({ title: t('settings.contrastFailToast'), icon: 'none' })
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
  syncDraft()
  setNavTitle('settings.title')
})

onHide(() => {
  // 未保存的预览在离开页面时回退为已持久化主题
  theme.apply()
})
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: var(--hz-space-lg);
  box-sizing: border-box;
}
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.section-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
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
.btn {
  flex: 1;
  height: 40px;
  line-height: 40px;
  font-size: var(--hz-font-size-sm);
}
.btn.primary {
  background: var(--hz-color-primary);
  color: #fff;
}
.btn.primary.disabled {
  opacity: 0.5;
}
.btn.ghost {
  background: transparent;
  color: var(--hz-color-text);
  border: 1px solid var(--hz-color-border);
}
</style>
