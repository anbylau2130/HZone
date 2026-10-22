<template>
  <!-- 日志阅读视图：单篇沉浸阅读，左右滑动/按钮/键盘翻篇，接近末尾预加载 -->
  <view class="reader">
    <Transition name="reader-slide" mode="out-in">
      <view
        :key="current.id"
        class="article"
        @touchstart="onTouchStart"
        @touchend="onTouchEnd"
      >
        <text class="r-title">{{ current.title }}</text>
        <view class="r-meta">
          <text class="r-meta-item">{{ formatDateTime(current.createdAt) }}</text>
          <text v-if="lunarLine" class="r-meta-item">{{ lunarLine }}</text>
          <text v-if="weatherLine" class="r-meta-item">{{ weatherLine }}</text>
        </view>
        <view class="r-divider" />
        <MarkdownBody :markdown="current.markdownBody ?? ''" />
      </view>
    </Transition>

    <view class="r-nav">
      <button class="r-btn" :disabled="index === 0" @click="prev">
        {{ $t('journals.readerPrev') }}
      </button>
      <text class="r-count">{{ index + 1 }} / {{ items.length }}{{ finished ? '' : '+' }}</text>
      <button class="r-btn" :disabled="atEnd" @click="next">
        {{ $t('journals.readerNext') }}
      </button>
    </view>
    <view v-if="loading" class="r-hint">
      <text class="r-hint-text">{{ $t('common.loading') }}</text>
    </view>
    <view v-else-if="finished && index === items.length - 1" class="r-hint">
      <text class="r-hint-text">{{ $t('common.noMore') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import MarkdownBody from './MarkdownBody.vue'
import { formatDateTime } from '../utils/format'
import { formatLunar } from '../utils/lunar'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import type { ContentItem } from '../api/endpoints'

const props = defineProps<{
  items: ContentItem[]
  finished: boolean
  loading: boolean
}>()
const emit = defineEmits<{ (e: 'load-more'): void }>()

// 默认最新一篇（列表按时间倒序，index 0 即最近）；next = 更早，prev = 更新
const index = ref(0)
const pendingAdvance = ref(false)

const current = computed(
  () => props.items[Math.min(index.value, props.items.length - 1)] ?? props.items[0]
)

const lunarLine = computed(() => formatLunar(current.value?.lunar))
const weatherLine = computed(() => {
  const w = current.value?.weather
  if (!w?.city) return ''
  const icon = CONDITION_ICONS[w.condition as WeatherCondition] ?? ''
  const temp = w.tempC !== null && w.tempC !== undefined ? Math.round(w.tempC) + '°C' : ''
  return [icon, w.city, temp].filter(Boolean).join(' ')
})

const atEnd = computed(() => index.value >= props.items.length - 1 && props.finished)

function afterChange() {
  uni.pageScrollTo({ scrollTop: 0, duration: 200 })
}

function prev() {
  if (index.value > 0) {
    index.value--
    afterChange()
  }
}

function next() {
  if (index.value < props.items.length - 1) {
    index.value++
    afterChange()
    return
  }
  // 已到已加载末尾但还有更多：先拉下一页，加载完成自动前进
  if (!props.finished) {
    pendingAdvance.value = true
    emit('load-more')
  }
}

// 列表变化（追加/refresh 重置）：夹紧索引；末尾点击的"待前进"在此兑现
watch(
  () => props.items.length,
  (len) => {
    if (pendingAdvance.value && len > index.value + 1) {
      pendingAdvance.value = false
      index.value++
      afterChange()
    }
    if (index.value >= len) {
      index.value = Math.max(0, len - 1)
    }
  }
)

// 预取：距已加载末尾 ≤3 篇即拉下一页（翻到时早已就绪）
watch(
  [index, () => props.items.length, () => props.finished],
  ([i, len, done]) => {
    if (!done && len - (i + 1) <= 3) emit('load-more')
  },
  { immediate: true }
)

// 横滑翻篇（水平位移 ≥60px 且明显大于垂直位移，不与页面滚动打架）
let tsX = 0
let tsY = 0
function onTouchStart(e: { changedTouches: { clientX: number; clientY: number }[] }) {
  const t = e.changedTouches[0]
  tsX = t.clientX
  tsY = t.clientY
}

function onTouchEnd(e: { changedTouches: { clientX: number; clientY: number }[] }) {
  const t = e.changedTouches[0]
  const dx = t.clientX - tsX
  const dy = t.clientY - tsY
  if (Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) {
    if (dx < 0) next()
    else prev()
  }
}

// 桌面键盘 ←/→ 翻篇（组件仅在阅读视图渲染，卸载即解绑）
function onKey(e: KeyboardEvent) {
  if (e.key === 'ArrowLeft') prev()
  else if (e.key === 'ArrowRight') next()
}
onMounted(() => document.addEventListener('keydown', onKey))
onBeforeUnmount(() => document.removeEventListener('keydown', onKey))
</script>

<style scoped>
.reader {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 阅读卡：大留白 + 大标题，沉浸感优先 */
.article {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 24px 22px 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.r-title {
  font-size: 22px;
  font-weight: 700;
  line-height: 1.4;
  color: var(--hz-color-text);
}
.r-meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.r-meta-item {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.r-divider {
  height: 1px;
  background: var(--hz-color-border);
}
/* 翻篇过渡：轻微右移淡入（像翻向下一篇） */
.reader-slide-enter-active,
.reader-slide-leave-active {
  transition: opacity 0.18s ease, transform 0.18s ease;
}
.reader-slide-enter-from {
  opacity: 0;
  transform: translateX(20px);
}
.reader-slide-leave-to {
  opacity: 0;
  transform: translateX(-12px);
}
.r-nav {
  display: flex;
  align-items: center;
  gap: 10px;
}
.r-btn {
  flex: 1;
  height: 40px;
  line-height: 40px;
  font-size: var(--hz-font-size-base);
  background: var(--hz-color-surface);
  color: var(--hz-color-text);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
}
.r-btn[disabled] {
  opacity: 0.45;
}
.r-count {
  flex-shrink: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.r-hint {
  display: flex;
  justify-content: center;
  padding: 2px 0 8px;
}
.r-hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
@media (min-width: 768px) {
  .article {
    padding: 32px 36px 24px;
  }
  .r-title {
    font-size: 24px;
  }
}
</style>
