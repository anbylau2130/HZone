<template>
  <view class="page">
    <view class="topbar">
      <text class="title">{{ $t('photos.title') }}</text>
      <text class="count">{{ items.length }}/{{ total }}</text>
    </view>

    <view v-if="!items.length && !loading" class="hint">
      <text class="hint-text">{{ $t('photos.empty') }}</text>
    </view>

    <!-- 瀑布流（CSS columns）：图片按原始比例展示，大图大格小图小格 -->
    <view class="masonry">
      <view v-for="(p, i) in items" :key="p.id" class="cell" @click="preview(i)">
        <image
          class="img"
          :class="{ loading: !p.thumbUrl }"
          :src="p.thumbUrl"
          mode="widthFix"
        />
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
import { computed, reactive, ref } from 'vue'
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
      // reactive 包装：blob 异步回填必须走代理才触发重渲染
      //（此前普通对象改属性不重渲染，实测 30 格 0 图）
      const item = reactive<PhotoItem>({ id: it.id, thumbUrl: '' })
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
/* 瀑布流：CSS columns 三列，图片自然比例（widthFix 高度自适应），
   大图大格小图小格——比等大方格更协调（用户要求） */
.masonry {
  column-count: 3;
  column-gap: 4px;
}
.cell {
  break-inside: avoid;
  margin-bottom: 4px;
  border-radius: var(--hz-radius-sm);
  overflow: hidden;
  background: var(--hz-color-surface);
  cursor: pointer;
  display: block;
}
.img {
  width: 100%;
  display: block;
}
/* blob 未就位时的占位（防瀑布高度跳变） */
.img.loading {
  min-height: 120px;
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
