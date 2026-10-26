import { createSSRApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { i18n, restoreLocale } from './locale'
import { initTheme } from './stores/theme'

// PWA：H5 注册 Service Worker（可安装到主屏 + 静态资源离线缓存；
// SW 内部策略不碰 /api 与 /media——鉴权与私有内容绝不进缓存）。
// 仅生产构建注册：dev 下 vite 会把 /sw.js 当模块加载直接炸出错误浮层
// #ifdef H5
if (!import.meta.env.DEV && 'serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js').catch(() => {
      // 注册失败（如 HTTP 非 localhost）静默——PWA 是渐进增强
    })
  })
}
// #endif

export function createApp() {
  const app = createSSRApp(App)
  app.use(createPinia())
  app.use(i18n)
  restoreLocale()
  initTheme()
  return { app }
}
