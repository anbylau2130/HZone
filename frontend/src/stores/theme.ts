import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  baseColors,
  colorVarNames,
  type ColorTokens,
  type ThemeMode
} from '../theme/tokens'
import { getPreset, presets } from '../theme/presets'
import { applyTheme } from '../theme/applyTheme'
import { isValidHex } from '../utils/contrast'
import { schedulePreferencesSync } from '../utils/prefSync'

const STORAGE_KEY = 'hz_theme'

// 从 presets 动态派生（账号偏好下发值合法性检查用）——静态清单会在新增
// 预置后把新 id 全部 sanitize 成回退值（实测踩中：开发者主题切完即被打回）。
// 回退 'warm'：蜜杏暖阳预置已删（与暖橙过近），老偏好回落到视觉最近的暖橙
const PRESET_IDS = presets.map((p) => p.id)
const FALLBACK_PRESET = 'warm'

/** 任意来源（本地存储 / 账号偏好下发）的主题值 → 合法 PersistedTheme（PRD 6.9）。 */
function sanitizeTheme(input: unknown): PersistedTheme {
  const parsed = (input ?? {}) as Partial<PersistedTheme>
  return {
    mode: parsed.mode === 'dark' ? 'dark' : 'light',
    presetId: PRESET_IDS.includes(parsed.presetId ?? '') ? (parsed.presetId as string) : FALLBACK_PRESET,
    overrides: sanitizeOverrides(parsed.overrides)
  }
}

export interface PersistedTheme {
  mode: ThemeMode
  presetId: string
  overrides: Partial<ColorTokens>
}

function readStoredTheme(): PersistedTheme {
  try {
    const raw = uni.getStorageSync(STORAGE_KEY) as string | ''
    if (!raw) return { mode: 'light', presetId: FALLBACK_PRESET, overrides: {} }
    return sanitizeTheme(JSON.parse(raw))
  } catch {
    return { mode: 'light', presetId: FALLBACK_PRESET, overrides: {} }
  }
}

function sanitizeOverrides(input: unknown): Partial<ColorTokens> {
  const out: Partial<ColorTokens> = {}
  const src = (input ?? {}) as Record<string, unknown>
  for (const key of Object.keys(colorVarNames) as (keyof ColorTokens)[]) {
    const v = src[key]
    if (typeof v === 'string' && isValidHex(v)) out[key] = v
  }
  return out
}

function composeColors(mode: ThemeMode, presetId: string, overrides: Partial<ColorTokens>): ColorTokens {
  const colors = { ...baseColors[mode] }
  const preset = getPreset(presetId)
  if (preset) {
    const fromPreset =
      preset.mode === mode
        ? preset.tokens
        : {
            primary: preset.tokens.primary,
            accent: preset.tokens.accent,
            interactive: preset.tokens.interactive
          }
    Object.assign(colors, fromPreset)
  }
  Object.assign(colors, overrides)
  return colors
}

// 启动恢复：不依赖 pinia，在 main.ts createApp 时调用
export function initTheme() {
  const saved = readStoredTheme()
  applyTheme(composeColors(saved.mode, saved.presetId, saved.overrides))
}

export const useThemeStore = defineStore('theme', () => {
  const saved = readStoredTheme()
  const mode = ref<ThemeMode>(saved.mode)
  const presetId = ref(saved.presetId)
  const overrides = ref<Partial<ColorTokens>>(saved.overrides)

  const colors = computed<ColorTokens>(() => composeColors(mode.value, presetId.value, overrides.value))

  function persistLocal() {
    uni.setStorageSync(STORAGE_KEY, JSON.stringify({
      mode: mode.value,
      presetId: presetId.value,
      overrides: overrides.value
    }))
  }

  // 本地落盘后防抖上送账号偏好（未登录时 schedulePreferencesSync 直接跳过）
  function persist() {
    persistLocal()
    schedulePreferencesSync()
  }

  function apply() {
    applyTheme(colors.value)
  }

  // 实时预览：只写 CSS 变量，不落存储
  function preview(c: ColorTokens) {
    applyTheme(c)
  }

  function setMode(m: ThemeMode) {
    mode.value = m
    apply()
    persist()
  }

  function setPreset(id: string) {
    const p = getPreset(id)
    if (!p) return
    presetId.value = id
    overrides.value = {}
    mode.value = p.mode
    apply()
    persist()
  }

  function setOverrides(o: Partial<ColorTokens>) {
    overrides.value = sanitizeOverrides(o)
    apply()
    persist()
  }

  function reset() {
    mode.value = 'light'
    presetId.value = FALLBACK_PRESET
    overrides.value = {}
    apply()
    persist()
  }

  // 账号偏好下发后的应用入口（PRD 6.9）：登录/fetchMe 拉到 preferences 时调用。
  // 冲突策略：账号偏好优先于本地——直接覆盖本地存储，保持多端一致。
  // 只写本地（persistLocal），不再触发上送（数据源就是服务端，回送是无效循环）。
  function applyAccount(prefs: Record<string, unknown> | null | undefined) {
    if (!prefs || typeof prefs !== 'object') return
    const sanitized = sanitizeTheme(prefs)
    mode.value = sanitized.mode
    presetId.value = sanitized.presetId
    overrides.value = sanitized.overrides
    apply()
    persistLocal()
  }

  return { mode, presetId, overrides, colors, apply, preview, setMode, setPreset, setOverrides, reset, applyAccount }
})
