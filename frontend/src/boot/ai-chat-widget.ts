import { createApp } from 'vue'
import AiChatWidget from '../components/AiChatWidget.vue'
import { i18n } from '../locale'

/**
 * 全局 AI 对话悬浮球：以独立 Vue 岛挂到 body。
 * uni-app 的 App.vue 不允许模板，按页面挂载又会随路由切换卸载，
 * 独立岛 + 共享 i18n 实例可常驻所有页面；登录态经 hz-auth 窗口事件同步。
 */
export function mountAiChatWidget() {
  if (document.getElementById('hz-ai-island')) return
  const host = document.createElement('div')
  host.id = 'hz-ai-island'
  document.body.appendChild(host)
  createApp(AiChatWidget).use(i18n).mount(host)
}
