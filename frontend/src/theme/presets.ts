import type { ColorTokens, ThemeMode } from './tokens'

// 预置主题：用户装修起点（PRD 6.9）。tokens 为其原生模式下的完整配色；
// 切到另一模式时只带入品牌色（primary/accent/interactive），底色回退该模式基础配色。
// 浅色预置的 primary 取基础蜜黄 #e9a23b 的加深变体：原值对 surface 仅约 2.1:1，
// 达不到主色/卡片 ≥ 3:1 门槛，故预置用可作前景色的深一档（实测见下方各主色）。
export interface ThemePreset {
  id: string
  nameKey: string
  mode: ThemeMode
  tokens: ColorTokens
}

export const presets: ThemePreset[] = [
  {
    id: 'default',
    nameKey: 'theme.presetDefault',
    mode: 'light',
    tokens: {
      primary: '#b8791f',
      accent: '#c2543f',
      interactive: '#3e87b5',
      bg: '#fbf3e9',
      surface: '#fffdf9',
      text: '#3b3128',
      textSecondary: '#8a7a66',
      border: '#efe2ce'
    }
  },
  {
    id: 'warm',
    nameKey: 'theme.presetWarm',
    mode: 'light',
    tokens: {
      primary: '#b85c1e',
      accent: '#c2543f',
      interactive: '#3e87b5',
      bg: '#faf1e4',
      surface: '#fffdf9',
      text: '#3b3128',
      textSecondary: '#8a7a66',
      border: '#eeddc6'
    }
  },
  {
    id: 'green',
    nameKey: 'theme.presetGreen',
    mode: 'light',
    tokens: {
      primary: '#12835a',
      accent: '#b8791f',
      interactive: '#3e87b5',
      bg: '#eef5ee',
      surface: '#f7fcf7',
      text: '#1d2b22',
      textSecondary: '#6b7f70',
      border: '#d9e6dc'
    }
  },
  {
    id: 'space',
    nameKey: 'theme.presetSpace',
    mode: 'dark',
    tokens: {
      primary: '#9d7bff',
      accent: '#ff7ac6',
      interactive: '#5fa3cc',
      bg: '#14121f',
      surface: '#1e1b2e',
      text: '#ece9f7',
      textSecondary: '#a49cc4',
      border: '#322d4a'
    }
  }
]

export function getPreset(id: string): ThemePreset | null {
  return presets.find((p) => p.id === id) ?? null
}
