import { getToken } from '../api/client'

let timer: ReturnType<typeof setTimeout> | null = null

/**
 * 账号级偏好同步（PRD 6.9）。
 *
 * 写入侧：本地改动（setMode/setPreset/setOverrides/reset/setLocale）都已先落本地存储
 * （hz_theme / hz_locale），这里防抖 1s 后整包（mode/presetId/overrides/locale）PUT
 * /me/preferences。后端整包落库，所以每次都带上全部字段。
 *
 * 未登录直接跳过；请求失败（离线/401 等）静默，不打扰——本地改动已生效。
 * 读取侧（登录/fetchMe 拉到 preferences 后的应用）在 stores/theme.ts applyAccount
 * 与 locale/index.ts applyLocalePreference——账号偏好优先于本地。
 */
export function schedulePreferencesSync() {
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => { void fireSync() }, 1000)
}

async function fireSync() {
  timer = null
  if (!getToken()) return
  let mode = 'light'
  let presetId = 'default'
  let overrides: Record<string, string> = {}
  let locale = 'zh-CN'
  try {
    const theme = JSON.parse((uni.getStorageSync('hz_theme') as string) || '{}') as Record<string, unknown>
    if (theme.mode === 'dark') mode = 'dark'
    if (typeof theme.presetId === 'string') presetId = theme.presetId
    if (theme.overrides && typeof theme.overrides === 'object') overrides = theme.overrides as Record<string, string>
  } catch {
    // hz_theme 损坏时按默认值上送，不抛错
  }
  try {
    const saved = uni.getStorageSync('hz_locale') as string
    if (saved === 'zh-CN' || saved === 'en-US') locale = saved
  } catch {
    // storage 不可用时按默认中文上送
  }
  // 直接 uni.request 而非 api/client 的 request：401 时不触发"清会话回登录页"的副作用，
  // 偏好同步是后台静默动作，失败不打扰。
  uni.request({
    url: '/api/me/preferences',
    method: 'PUT',
    data: { mode, presetId, overrides, locale },
    header: { Authorization: `Bearer ${getToken()}` },
    success: () => {},
    fail: () => {}
  })
}
