import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import {
  baseColors,
  colorVarNames,
  type ColorTokens,
  type ThemeMode
} from '../theme/tokens'
import { getPreset } from '../theme/presets'
import { applyTheme } from '../theme/applyTheme'
import { isValidHex } from '../utils/contrast'

const STORAGE_KEY = 'hz_theme'

export interface PersistedTheme {
  mode: ThemeMode
  presetId: string
  overrides: Partial<ColorTokens>
}

function readStoredTheme(): PersistedTheme {
  try {
    const raw = uni.getStorageSync(STORAGE_KEY) as string | ''
    if (!raw) return { mode: 'light', presetId: 'default', overrides: {} }
    const parsed = JSON.parse(raw) as Partial<PersistedTheme>
    return {
      mode: parsed.mode === 'dark' ? 'dark' : 'light',
      presetId: ['default', 'warm', 'green', 'space'].includes(parsed.presetId ?? '')
        ? (parsed.presetId as string)
        : 'default',
      overrides: sanitizeOverrides(parsed.overrides)
    }
  } catch {
    return { mode: 'light', presetId: 'default', overrides: {} }
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

  function persist() {
    uni.setStorageSync(STORAGE_KEY, JSON.stringify({
      mode: mode.value,
      presetId: presetId.value,
      overrides: overrides.value
    }))
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
    presetId.value = 'default'
    overrides.value = {}
    apply()
    persist()
  }

  return { mode, presetId, overrides, colors, apply, preview, setMode, setPreset, setOverrides, reset }
})
