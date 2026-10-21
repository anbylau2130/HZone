import { createI18n } from 'vue-i18n'
import { schedulePreferencesSync } from '../utils/prefSync'
import zhCN from './zh-CN.json'
import enUS from './en-US.json'

export type AppLocale = 'zh-CN' | 'en-US'

export const i18n = createI18n<false>({
  legacy: false,
  locale: 'zh-CN',
  fallbackLocale: 'zh-CN',
  messages: { 'zh-CN': zhCN, 'en-US': enUS }
})

const LOCALE_KEY = 'hz_locale'
const LOCALES: AppLocale[] = ['zh-CN', 'en-US']

export function restoreLocale() {
  try {
    const saved = uni.getStorageSync(LOCALE_KEY) as string | ''
    if (LOCALES.includes(saved as AppLocale)) {
      i18n.global.locale.value = saved as AppLocale
    }
  } catch {
    // storage 不可用时保持默认中文
  }
}

export function setLocale(locale: AppLocale) {
  i18n.global.locale.value = locale
  uni.setStorageSync(LOCALE_KEY, locale)
  // 账号级同步（PRD 6.9）：语言改动防抖上送（未登录时静默跳过）
  schedulePreferencesSync()
}

// 账号偏好下发后的语言应用（账号偏好优先于本地）：仅接受合法值。
export function applyLocalePreference(locale: unknown) {
  if (locale === 'zh-CN' || locale === 'en-US') {
    setLocale(locale)
  }
}

// 页面 onShow 时刷新导航栏标题，配合语言切换
export function setNavTitle(key: string) {
  uni.setNavigationBarTitle({ title: i18n.global.t(key) })
}
