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

  async function load(reset: boolean) {
    if (loading.value) return
    if (loaded.value && !reset && finished.value) return
    loading.value = true
    try {
      const page = await fetcher(reset ? 0 : items.value.length, pageSize)
      items.value = reset ? page.items : [...items.value, ...page.items]
      total.value = page.total
      loaded.value = true
    } finally {
      loading.value = false
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
