import { onMounted, onUnmounted } from 'vue'

/**
 * 滚动加载（window 滚动）：uni-app H5 的页面级滚动宿主就是 window
 * （页面容器自身没有 overflow，uni-app 把页面铺在 document 流里），
 * 因此不做容器探测，直接监听 window scroll/resize。
 *
 * 用法：useInfiniteScroll(() => { if (!finished.value && !loading.value) loadMore() })
 * 距底部 ≤300px 时触发回调；回调自身负责防重入（usePagination.loadMore
 * 已有 in-flight / finished 短路，这里不重复维护状态）。
 */
export function useInfiniteScroll(onNearBottom: () => void, threshold = 300) {
  let bound = false

  // 距底距离 = 文档总高 - 视口高 - 已滚距离；≤阈值即视为"滚近底部"
  function check() {
    const doc = document.documentElement
    const distance = doc.scrollHeight - window.innerHeight - window.scrollY
    if (distance <= threshold) onNearBottom()
  }

  function mount() {
    if (bound) return
    bound = true
    // passive：滚动监听不需要 preventDefault，让合成器线程不被阻塞
    window.addEventListener('scroll', check, { passive: true })
    window.addEventListener('resize', check, { passive: true })
    // 首次挂载若内容不足一屏，scroll 事件永远不触发——主动检测一次，
    // 防止短列表"永远填不满首屏"的假死（回调自身防重入，多触发无害）
    check()
  }

  function destroy() {
    if (!bound) return
    bound = false
    window.removeEventListener('scroll', check)
    window.removeEventListener('resize', check)
  }

  onMounted(mount)
  onUnmounted(destroy)

  // destroy 供需要手动控制生命周期的地方使用（正常情况下自动挂载/卸载）
  return { destroy, check }
}
