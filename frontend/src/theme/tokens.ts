// design token 定义（PRD 6.9）：颜色 token 可被主题/覆盖改写，结构 token（圆角/字号/间距）固定
export type ThemeMode = 'light' | 'dark'

export interface ColorTokens {
  primary: string
  accent: string
  bg: string
  surface: string
  text: string
  textSecondary: string
  border: string
}

export const colorVarNames: Record<keyof ColorTokens, string> = {
  primary: '--hz-color-primary',
  accent: '--hz-color-accent',
  bg: '--hz-color-bg',
  surface: '--hz-color-surface',
  text: '--hz-color-text',
  textSecondary: '--hz-color-text-secondary',
  border: '--hz-color-border'
}

// 浅/深两套基础配色（预置主题基于此叠加）
export const baseColors: Record<ThemeMode, ColorTokens> = {
  light: {
    primary: '#4f7cff',
    accent: '#e54d42',
    bg: '#f7f8fa',
    surface: '#ffffff',
    text: '#1f2329',
    textSecondary: '#646a73',
    border: '#e5e6eb'
  },
  dark: {
    primary: '#5c8aff',
    accent: '#ff6b60',
    bg: '#101114',
    surface: '#1b1d22',
    text: '#e8eaed',
    textSecondary: '#9aa0a6',
    border: '#2c2f36'
  }
}

// 非颜色 token：主题切换不变，与 App.vue :root 默认值保持一致
export const staticTokens: Record<string, string> = {
  '--hz-radius-sm': '8px',
  '--hz-radius-md': '12px',
  '--hz-radius-lg': '20px',
  '--hz-font-size-sm': '13px',
  '--hz-font-size-base': '15px',
  '--hz-font-size-lg': '18px',
  '--hz-space-sm': '8px',
  '--hz-space-md': '12px',
  '--hz-space-lg': '16px'
}
