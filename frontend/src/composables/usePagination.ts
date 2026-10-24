import { computed, ref } from 'vue'
import type { Ref } from 'vue'

export interface Page<T> {
  items: T[]
  total: number
}

/**
 * 分页加载通用逻辑：refresh() 从头加载，loadMore() 追加下一页。
 * fetcher 由调用方提供（可以读 kind / 搜索词等外部状态做闭包）。
 */
export function usePagination<T>(
  fetcher: (offset: number, limit: number) => Promise<Page<T>>,
  pageSize = 20
) {
  const items = ref([]) as Ref<T[]>
  const total = ref(0)
  const loaded = ref(false)
  const loading = ref(false)

  const finished = computed(() => loaded.value && items.value.length >= total.value)

  // 请求序号（竞态防护）：loadMore 在途时切筛选/搜索触发 refresh，旧 loadMore 响应
  // 晚到会覆盖新列表——refresh 不做 loading 短路（直接打断在途请求发新的），
  // 每个响应落地前比对序号，过期响应整体丢弃，不写 items/total/finished
  let requestSeq = 0

  async function load(reset: boolean) {
    // loadMore 保持防重入；refresh 例外——必须能打断在途请求立刻生效
    if (!reset && loading.value) return
    if (loaded.value && !reset && finished.value) return
    const seq = ++requestSeq
    loading.value = true
    try {
      const page = await fetcher(reset ? 0 : items.value.length, pageSize)
      // 过期响应（已被更新的请求取代）：整体丢弃
      if (seq !== requestSeq) return
      items.value = reset ? page.items : [...items.value, ...page.items]
      total.value = page.total
      loaded.value = true
    } catch {
      // 失败静默（提示由请求层统一 toast）：loading 由 finally 复位、
      // loaded 保持 false → 滚动/翻页仍可重试，调用方无 unhandled rejection
    } finally {
      // 只有最新请求才动 loading：被打断的旧请求结束后新请求仍在途，不能误清
      if (seq === requestSeq) loading.value = false
    }
  }

  return {
    items,
    total,
    loading,
    finished,
    refresh: () => load(true),
    loadMore: () => load(false)
  }
}
