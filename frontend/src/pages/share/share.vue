<template>
  <!-- 公开只读分享页：无登录态可访问（token 即凭证）。极简：作者+标题/正文+日期天气 -->
  <view class="page">
    <view v-if="loading" class="hint"><text class="hint-text">{{ $t('common.loading') }}</text></view>
    <view v-else-if="!item" class="hint">
      <text class="hint-text">{{ $t('share.notFound') }}</text>
    </view>
    <template v-else>
      <view class="head">
        <text class="author">{{ item.authorNickname }}</text>
        <text class="date">{{ item.createdAt.slice(0, 10) }}</text>
      </view>
      <view class="card">
        <text v-if="item.title" class="title">{{ item.title }}</text>
        <view v-if="weatherLine" class="weather"><text class="weather-text">{{ weatherLine }}</text></view>
        <text v-if="item.kind === 'say'" class="say-text">{{ item.text }}</text>
        <!-- 笔记正文：cherry previewOnly（XSS 评估通过，.scratch/security） -->
        <div v-else class="md-preview">
          <AsyncCherryEditor :model-value="item.markdownBody ?? ''" preview-only />
        </div>
        <view v-if="(item.media?.length ?? 0) > 0" class="media-grid">
          <image
            v-for="(m, i) in item.media"
            :key="i"
            class="media-img"
            :src="m.url"
            mode="aspectFill"
          />
        </view>
      </view>
      <text class="footer">{{ $t('share.footer') }}</text>
    </template>
  </view>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

const AsyncCherryEditor = defineAsyncComponent(() => import('../../components/CherryEditor.vue'))

interface SharedPayload {
  kind: 'say' | 'journal'
  authorNickname: string
  text: string | null
  title: string | null
  markdownBody: string | null
  createdAt: string
  weather: { city: string | null; condition: string | null; tempC: number | null } | null
  media: { url: string; kind: string }[]
}

const item = ref<SharedPayload | null>(null)
const loading = ref(true)

const weatherLine = computed(() => {
  const w = item.value?.weather
  if (!w || (!w.condition && w.tempC == null && !w.city)) return ''
  return [w.city, w.tempC != null ? `${Math.round(w.tempC)}°C` : null].filter(Boolean).join(' · ')
})

onLoad((query) => {
  setNavTitle('share.title')
  const token = ((query ?? {}) as Record<string, string>).token ?? ''
  if (!token) {
    loading.value = false
    return
  }
  fetch(`/public/shared/${encodeURIComponent(token)}`)
    .then((r) => (r.ok ? r.json() : null))
    .then((j: SharedPayload | null) => {
      item.value = j
    })
    .catch(() => {
      item.value = null
    })
    .finally(() => {
      loading.value = false
    })
})
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 20px 16px calc(40px + env(safe-area-inset-bottom));
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  padding: 0 4px;
}
.author {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.date {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.title {
  font-size: 20px;
  font-weight: 700;
  color: var(--hz-color-text);
  line-height: 1.4;
}
.weather-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.say-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.7;
  white-space: pre-wrap;
}
.media-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 4px;
}
.media-img {
  width: 100%;
  aspect-ratio: 1;
  border-radius: var(--hz-radius-sm);
}
.footer {
  text-align: center;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  opacity: 0.7;
}
.hint {
  padding: 60px 0;
  display: flex;
  justify-content: center;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
