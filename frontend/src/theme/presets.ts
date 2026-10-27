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
  },
  // ---------- 开发者系列（用户要求）：配色取自各家官方 palette，
  // 对比度按项目门槛校准（primary 上白字 ≥3:1，正文/链接 ≥4.5:1） ----------
  {
    // GitHub Light Primer：#f6f8fa 画布、绿主按钮、链接蓝
    id: 'github-light',
    nameKey: 'theme.presetGithubLight',
    mode: 'light',
    tokens: {
      primary: '#1f883d',
      accent: '#8250df',
      interactive: '#0969da',
      bg: '#f6f8fa',
      surface: '#ffffff',
      text: '#1f2328',
      textSecondary: '#656d76',
      border: '#d0d7de'
    }
  },
  {
    // GitHub Dark Primer：#0d1117 画布、按钮绿加深、链接亮蓝
    id: 'github-dark',
    nameKey: 'theme.presetGithubDark',
    mode: 'dark',
    tokens: {
      primary: '#238636',
      accent: '#bc8cff',
      interactive: '#58a6ff',
      bg: '#0d1117',
      surface: '#161b22',
      text: '#e6edf3',
      textSecondary: '#8b949e',
      border: '#30363d'
    }
  },
  {
    // JetBrains Darcula：#2b2b2b 编辑器底、品牌橙主按钮、链接蓝
    id: 'darcula',
    nameKey: 'theme.presetDarcula',
    mode: 'dark',
    tokens: {
      primary: '#f74c00',
      accent: '#ffc66d',
      interactive: '#589df6',
      bg: '#2b2b2b',
      surface: '#3c3f41',
      text: '#a9b7c6',
      textSecondary: '#8a8a8a',
      border: '#515355'
    }
  },
  {
    // VS Code Dark+：#1e1e1e 编辑器底、按钮蓝、链接亮蓝、关键字紫点缀
    id: 'vscode-dark',
    nameKey: 'theme.presetVscodeDark',
    mode: 'dark',
    tokens: {
      primary: '#0e639c',
      accent: '#c586c0',
      interactive: '#3794ff',
      bg: '#1e1e1e',
      surface: '#252526',
      text: '#d4d4d4',
      textSecondary: '#858585',
      border: '#3c3c3c'
    }
  },
  {
    // VS Code Light+：白画布、现代蓝按钮/链接
    id: 'vscode-light',
    nameKey: 'theme.presetVscodeLight',
    mode: 'light',
    tokens: {
      primary: '#005fb8',
      accent: '#af00db',
      interactive: '#005fb8',
      bg: '#ffffff',
      surface: '#f3f3f3',
      text: '#333333',
      textSecondary: '#767676',
      border: '#e0e0e0'
    }
  },
  // ---------- 经典编辑器/终端系列（官方 palette + 项目对比度门槛校准：
  // primary 上白字 ≥3:1、链接 on bg ≥4.5:1；官方亮色（Nord 冰蓝/Dracula 紫/
  // Monokai 绿等）作按钮底不达白字门槛，取其加深档，点缀/链接保留原亮色） ----------
  {
    // Nord 极地：北欧蓝灰，冷冽低饱和
    id: 'nord',
    nameKey: 'theme.presetNord',
    mode: 'dark',
    tokens: {
      primary: '#5e81ac',
      accent: '#b48ead',
      interactive: '#88c0d0',
      bg: '#2e3440',
      surface: '#3b4252',
      text: '#d8dee9',
      textSecondary: '#8f99ae',
      border: '#4c566a'
    }
  },
  {
    // Dracula：紫粉青经典暗色
    id: 'dracula',
    nameKey: 'theme.presetDracula',
    mode: 'dark',
    tokens: {
      primary: '#6272a4',
      accent: '#ff79c6',
      interactive: '#8be9fd',
      bg: '#282a36',
      surface: '#343949',
      text: '#f8f8f2',
      textSecondary: '#a6acce',
      border: '#44475a'
    }
  },
  {
    // Atom One Dark：均衡灰蓝
    id: 'one-dark',
    nameKey: 'theme.presetOneDark',
    mode: 'dark',
    tokens: {
      primary: '#3e72af',
      accent: '#c678dd',
      interactive: '#61afef',
      bg: '#282c34',
      surface: '#2c313a',
      text: '#abb2bf',
      textSecondary: '#7f8794',
      border: '#3e4451'
    }
  },
  {
    // Rosé Pine：玫瑰金文艺暗色
    id: 'rose-pine',
    nameKey: 'theme.presetRosePine',
    mode: 'dark',
    tokens: {
      primary: '#b4637a',
      accent: '#c4a7e7',
      interactive: '#9ccfd8',
      bg: '#191724',
      surface: '#1f1d2e',
      text: '#e0def4',
      textSecondary: '#908caa',
      border: '#26233a'
    }
  },
  {
    // Monokai：Sublime 经典荧光绿粉
    id: 'monokai',
    nameKey: 'theme.presetMonokai',
    mode: 'dark',
    tokens: {
      primary: '#5e9418',
      accent: '#f92672',
      interactive: '#66d9ef',
      bg: '#272822',
      surface: '#30322c',
      text: '#f8f8f2',
      textSecondary: '#a0a298',
      border: '#3e3d32'
    }
  },
  {
    // Solarized Light：护眼米黄纸感
    id: 'solarized-light',
    nameKey: 'theme.presetSolarized',
    mode: 'light',
    tokens: {
      primary: '#cb4b16',
      accent: '#d33682',
      interactive: '#1a7db0',
      bg: '#f7f0dd',
      surface: '#fdf6e3',
      text: '#073642',
      textSecondary: '#657b83',
      border: '#e6ddc0'
    }
  }
]

export function getPreset(id: string): ThemePreset | null {
  return presets.find((p) => p.id === id) ?? null
}
