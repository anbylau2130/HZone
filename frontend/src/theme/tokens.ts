// design token 定义（PRD 6.9）：颜色 token 可被主题/覆盖改写，结构 token（圆角/字号/间距）固定
export type ThemeMode = 'light' | 'dark'

export interface ColorTokens {
  primary: string
  accent: string
  interactive: string
  bg: string
  surface: string
  text: string
  textSecondary: string
  border: string
}

export const colorVarNames: Record<keyof ColorTokens, string> = {
  primary: '--hz-color-primary',
  accent: '--hz-color-accent',
  interactive: '--hz-color-interactive',
  bg: '--hz-color-bg',
  surface: '--hz-color-surface',
  text: '--hz-color-text',
  textSecondary: '--hz-color-text-secondary',
  border: '--hz-color-border'
}

// 浅/深两套基础配色：浅色「蜜杏暖阳」/ 深色「暖夜」。
// primary 蜜黄只做按钮底色（白字），文字链/次级按钮用 interactive 天青保证可读对比。
export const baseColors: Record<ThemeMode, ColorTokens> = {
  light: {
    primary: '#e9a23b',
    accent: '#c2543f',
    interactive: '#3e87b5',
    bg: '#fbf3e9',
    surface: '#fffdf9',
    text: '#3b3128',
    textSecondary: '#8a7a66',
    border: '#efe2ce'
  },
  dark: {
    primary: '#f0b45a',
    accent: '#d96b55',
    interactive: '#5fa3cc',
    bg: '#1e1712',
    surface: '#2a211a',
    text: '#ede4d7',
    textSecondary: '#a8977f',
    border: '#3a2f24'
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
