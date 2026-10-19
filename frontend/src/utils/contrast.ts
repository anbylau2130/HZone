// WCAG 对比度校验（PRD 6.9）：自定义配色保存前必须通过
const HEX_RE = /^#[0-9a-fA-F]{6}$/

export function isValidHex(hex: string): boolean {
  return HEX_RE.test(hex)
}

export function hexToRgb(hex: string): [number, number, number] | null {
  if (!isValidHex(hex)) return null
  return [
    parseInt(hex.slice(1, 3), 16),
    parseInt(hex.slice(3, 5), 16),
    parseInt(hex.slice(5, 7), 16)
  ]
}

// WCAG 相对亮度
export function relativeLuminance(hex: string): number | null {
  const rgb = hexToRgb(hex)
  if (!rgb) return null
  const lin = rgb.map((c) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4)
  })
  return 0.2126 * lin[0] + 0.7152 * lin[1] + 0.0722 * lin[2]
}

export function contrastRatio(hexA: string, hexB: string): number {
  const la = relativeLuminance(hexA)
  const lb = relativeLuminance(hexB)
  if (la === null || lb === null) return 0
  const [hi, lo] = la >= lb ? [la, lb] : [lb, la]
  return (hi + 0.05) / (lo + 0.05)
}

export interface ContrastCheck {
  ok: boolean
  textOnSurface: number
  primaryOnSurface: number
}

// 门槛：文字/卡片 ≥ 4.5:1（正文可读），主色/卡片 ≥ 3:1（按钮/链接可辨识）
export const TEXT_CONTRAST_MIN = 4.5
export const PRIMARY_CONTRAST_MIN = 3

export function checkContrast(colors: {
  text: string
  surface: string
  primary: string
}): ContrastCheck {
  const textOnSurface = contrastRatio(colors.text, colors.surface)
  const primaryOnSurface = contrastRatio(colors.primary, colors.surface)
  return {
    ok: textOnSurface >= TEXT_CONTRAST_MIN && primaryOnSurface >= PRIMARY_CONTRAST_MIN,
    textOnSurface,
    primaryOnSurface
  }
}
