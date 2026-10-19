import { createSSRApp } from 'vue'
import { createPinia } from 'pinia'
import App from './App.vue'
import { i18n, restoreLocale } from './locale'
import { initTheme } from './stores/theme'

export function createApp() {
  const app = createSSRApp(App)
  app.use(createPinia())
  app.use(i18n)
  restoreLocale()
  initTheme()
  return { app }
}
