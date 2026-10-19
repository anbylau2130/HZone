<template>
  <view class="page">
    <view class="filters">
      <button
        v-for="k in kinds"
        :key="k.value"
        class="filter"
        :class="{ on: kind === k.value }"
        @click="setKind(k.value)"
      >
        {{ $t(k.label) }}
      </button>
    </view>

    <view v-if="pagination.loading.value && !items.length" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="!items.length" class="hint">
      <text class="hint-text">{{ $t('common.empty') }}</text>
    </view>

    <ContentCard v-for="c in items" :key="c.id" :content="c" @open="openDetail" />

    <view v-if="items.length" class="more">
      <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
      <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
        {{ $t('common.loadMore') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import ContentCard from '../../components/ContentCard.vue'
import { usePagination } from '../../composables/usePagination'
import { listMyContents, type ContentItem, type KindFilter } from '../../api/endpoints'

const kinds: { value: KindFilter; label: string }[] = [
  { value: '', label: 'timeline.filterAll' },
  { value: 'say', label: 'timeline.kindSay' },
  { value: 'journal', label: 'timeline.kindJournal' }
]

const kind = ref<KindFilter>('')

const pagination = usePagination<ContentItem>((offset, limit) =>
  listMyContents(kind.value, offset, limit)
)
const { items, finished, loadMore } = pagination

onLoad((query) => {
  const q = (query ?? {}) as Record<string, string>
  if (q.kind === 'say' || q.kind === 'journal') {
    kind.value = q.kind
  }
  pagination.refresh()
})

function setKind(k: KindFilter) {
  if (kind.value === k) return
  kind.value = k
  pagination.refresh()
}

function openDetail(c: ContentItem) {
  uni.navigateTo({ url: `/pages/detail/detail?id=${c.id}` })
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
.filters {
  display: flex;
  gap: 8px;
}
.filter {
  flex: 1;
  height: 36px;
  line-height: 36px;
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  border: 1px solid var(--hz-color-border);
}
.filter.on {
  background: var(--hz-color-primary);
  color: #fff;
  border-color: var(--hz-color-primary);
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
</style>
