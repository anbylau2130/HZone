import type { ColorTokens, ThemeMode } from './tokens'

// 预置主题：用户装修起点（PRD 6.9）。tokens 为其原生模式下的完整配色；
// 切到另一模式时只带入品牌色（primary/accent），底色回退该模式基础配色。
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
      primary: '#4f7cff',
      accent: '#e54d42',
      bg: '#f7f8fa',
      surface: '#ffffff',
      text: '#1f2329',
      textSecondary: '#646a73',
      border: '#e5e6eb'
    }
  },
  {
    id: 'warm',
    nameKey: 'theme.presetWarm',
    mode: 'light',
    tokens: {
      primary: '#ea6202',
      accent: '#d6336c',
      bg: '#faf6f1',
      surface: '#fffdfa',
      text: '#2d2016',
      textSecondary: '#8c7f6e',
      border: '#eee3d6'
    }
  },
  {
    id: 'green',
    nameKey: 'theme.presetGreen',
    mode: 'light',
    tokens: {
      primary: '#1a9e6b',
      accent: '#e8a33d',
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
