<template>
  <view class="page">
    <view class="topbar">
      <text class="title">{{ $t('photos.title') }}</text>
      <text class="count">{{ items.length }}/{{ total }}</text>
    </view>

    <view v-if="!items.length && !loading" class="hint">
      <text class="hint-text">{{ $t('photos.empty') }}</text>
    </view>

    <!-- 3 列网格：缩略图（授权 blob）铺满，点格全屏预览（可滑动翻看全部） -->
    <view class="grid">
      <view v-for="(p, i) in items" :key="p.id" class="cell" @click="preview(i)">
        <image class="img" :src="p.thumbUrl" mode="aspectFill" />
      </view>
    </view>

    <view v-if="items.length && loading" class="hint">
      <text class="hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-if="items.length && finished" class="hint">
      <text class="hint-text">{{ $t('common.noMore') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { useInfiniteScroll } from '../../composables/useInfiniteScroll'
import { loadMediaUrl } from '../../api/media'
import { request } from '../../api/client'

interface PhotoItem {
  id: string
  thumbUrl: string
}

const PAGE_SIZE = 30
const items = ref<PhotoItem[]>([])
const total = ref(0)
const loading = ref(false)
const finished = computed(() => items.value.length >= total.value && total.value > 0)

onShow(() => {
  setNavTitle('photos.title')
  // 每次进入重拉第一页（新上传的图即时可见；blob 缓存在 loadMediaUrl 模块层）
  void loadPage(0)
})

async function loadPage(offset: number) {
  if (loading.value) return
  loading.value = true
  try {
    const res = await request<{
      items: { id: string; thumbStoredName: string }[]
      total: number
    }>(`/api/me/media/photos?offset=${offset}&limit=${PAGE_SIZE}`)
    total.value = res.total
    const fresh: PhotoItem[] = []
    for (const it of res.items) {
      const item: PhotoItem = { id: it.id, thumbUrl: '' }
      fresh.push(item)
      loadMediaUrl(`/media/${it.thumbStoredName}`)
        .then((u) => {
          item.thumbUrl = u
        })
        .catch(() => {})
    }
    items.value = offset === 0 ? fresh : [...items.value, ...fresh]
  } catch {
    // 请求层已 toast；保留已加载内容
  } finally {
    loading.value = false
  }
}

function preview(index: number) {
  const urls = items.value.map((p) => p.thumbUrl).filter(Boolean)
  if (!urls.length) return
  uni.previewImage({ current: urls[index], urls })
}

useInfiniteScroll(() => {
  if (!finished.value && !loading.value) void loadPage(items.value.length)
})
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 16px calc(70px + env(safe-area-inset-bottom));
  box-sizing: border-box;
}
.topbar {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 12px;
}
.title {
  font-size: 20px;
  font-weight: 700;
  color: var(--hz-color-text);
}
.count {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 4px;
}
.cell {
  aspect-ratio: 1;
  border-radius: var(--hz-radius-sm);
  overflow: hidden;
  background: var(--hz-color-surface);
  cursor: pointer;
}
.img {
  width: 100%;
  height: 100%;
  display: block;
}
.hint {
  padding: 32px 0;
  display: flex;
  justify-content: center;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
