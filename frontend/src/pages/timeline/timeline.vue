<template>
  <view class="page">
    <SegmentedFilter :model-value="kind" :options="kinds" @update:model-value="setKind" />

    <!-- 热力格点格下钻：date 参数激活按天过滤，点击清除回到原行为 -->
    <view v-if="date" class="date-bar" @click="clearDate">
      <text class="date-text">{{ $t('timeline.filterDate', { d: date }) }}</text>
      <text class="date-clear">{{ $t('timeline.clearDate') }}</text>
    </view>

    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :key="c.id" :content="c" @updated="onCardUpdated" @deleted="onCardDeleted" />

    <view v-if="items.length" class="more">
      <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
      <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
        {{ $t('common.loadMore') }}
      </button>
    </view>

    <AppSidebar current="mine" />
    <ToolDock />
    <AppTabBar current="mine" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import ContentCard from '../../components/ContentCard.vue'
import SegmentedFilter from '../../components/SegmentedFilter.vue'
import AppTabBar from '../../components/AppTabBar.vue'
import AppSidebar from '../../components/AppSidebar.vue'
import ToolDock from '../../components/ToolDock.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, type ContentItem, type KindFilter } from '../../api/endpoints'

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]

const kind = ref<KindFilter>('')

// 热力格点格下钻（homepage-v2 ticket 05）：date=yyyy-MM-dd 按天过滤；无参数=原行为。
const date = ref<string | null>(null)

const pagination = usePagination<ContentItem>((offset, limit) =>
  listMyContents(kind.value, offset, limit, date.value ?? undefined)
)
const { items, finished, loadMore } = pagination

onLoad((query) => {
  setNavTitle('hub.timeline')
  const q = (query ?? {}) as Record<string, string>
  if (q.kind === 'say' || q.kind === 'journal') {
    kind.value = q.kind
  }
  if (q.date && /^\d{4}-\d{2}-\d{2}$/.test(q.date)) {
    date.value = q.date
  }
  pagination.refresh()
})

function clearDate() {
  date.value = null
  pagination.refresh()
}

function setKind(k: string) {
  const next = k as KindFilter
  if (kind.value === next) return
  kind.value = next
  pagination.refresh()
}

// 列表卡上删除成功后移除本地列表项
function onCardDeleted(id: string) {
  items.value = items.value.filter((c) => c.id !== id)
}

// 卡片原地编辑保存成功后本地替换该项
function onCardUpdated(c: ContentItem) {
  items.value = items.value.map((x) => (x.id === c.id ? c : x))
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
.date-bar {
  display: flex;
  align-items: baseline;
  gap: 8px;
}
.date-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.date-clear {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.hint {
  padding: 40px 0;
  display: flex;
  justify-content: center;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
.pick {
  width: 60%;
  height: 38px;
  line-height: 38px;
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
@media (min-width: 768px) {
  .page {
    margin-left: 220px;
    padding-left: max(16px, calc((100% - 220px - 48rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 48rem) / 2));
  }
}
/* ≥1200 三栏壳：右侧为 300px 工具 dock 留位，内容在侧栏与 dock 之间居中 */
@media (min-width: 1200px) {
  .page {
    margin-right: 300px;
    padding-left: max(16px, calc((100% - 220px - 300px - 48rem) / 2));
    padding-right: max(16px, calc((100% - 220px - 300px - 48rem) / 2));
  }
}
</style>
