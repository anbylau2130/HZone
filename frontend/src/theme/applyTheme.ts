export type ThemeMode = 'light' | 'dark'

const light = {
  '--hz-color-primary': '#4f7cff',
  '--hz-color-bg': '#f7f8fa',
  '--hz-color-surface': '#ffffff',
  '--hz-color-text': '#1f2329',
  '--hz-color-text-secondary': '#646a73',
  '--hz-color-border': '#e5e6eb'
}

const dark = {
  '--hz-color-primary': '#5c8aff',
  '--hz-color-bg': '#101114',
  '--hz-color-surface': '#1b1d22',
  '--hz-color-text': '#e8eaed',
  '--hz-color-text-secondary': '#9aa0a6',
  '--hz-color-border': '#2c2f36'
}

export function applyTheme(mode: ThemeMode) {
  const tokens = mode === 'light' ? light : dark
  // #ifdef H5
  for (const [k, v] of Object.entries(tokens)) {
    document.documentElement.style.setProperty(k, v)
  }
  // #endif
}
