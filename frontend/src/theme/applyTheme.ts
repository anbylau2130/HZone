import { colorVarNames, staticTokens, type ColorTokens } from './tokens'

// 组合规则（base mode + 预置 + 用户覆盖）在 stores/theme.ts 完成；
// 本函数只负责把最终 token 写入 CSS 变量。
export function applyTheme(colors: ColorTokens) {
  const vars: Record<string, string> = { ...staticTokens }
  for (const [key, varName] of Object.entries(colorVarNames)) {
    const value = (colors as Record<string, string | undefined>)[key]
    if (value) vars[varName] = value
  }
  // #ifdef H5
  for (const [k, v] of Object.entries(vars)) {
    document.documentElement.style.setProperty(k, v)
  }
  // #endif
}
