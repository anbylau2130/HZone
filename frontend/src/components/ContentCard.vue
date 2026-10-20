<template>
  <view class="card" @click="$emit('open', content)">
    <view class="head">
      <view class="avatar">
        <!-- 票 05 接真头像：avatarUrl 有值走 image，否则回退昵称首字占位 -->
        <image
          v-if="content.author.avatarUrl"
          class="avatar-img"
          :src="content.author.avatarUrl"
          mode="aspectFill"
        />
        <text v-else class="avatar-fallback">{{ content.author.nickname.slice(0, 1) }}</text>
      </view>
      <view class="head-mid">
        <text class="author">{{ content.author.nickname }}</text>
        <text class="kind">{{ kindLabel }}</text>
      </view>
    </view>

    <text v-if="content.kind === 'journal'" class="title">{{ content.title }}</text>
    <text v-else class="text">{{ content.text }}</text>
    <MediaGrid v-if="hasMedia" :media="content.media" />
    <RecipientsBar :recipients="content.recipients" />

    <view class="meta">
      <text class="meta-time">
        {{ formatDateTime(content.createdAt) }} · {{ formatRelativeTime(content.createdAt) }}
        </text>
      <text v-if="weatherChip" class="weather-chip">{{ weatherChip }}</text>
      <view class="meta-spacer" />
      <CardOwnerActions v-if="isAuthor" @edit="goEdit" @delete="remove" />
    </view>

    <view class="foot">
      <text class="foot-item">{{ $t('detail.commentCount', { n: content.commentCount }) }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import MediaGrid from './MediaGrid.vue'
import RecipientsBar from './RecipientsBar.vue'
import CardOwnerActions from './CardOwnerActions.vue'
import { formatDateTime, formatRelativeTime } from '../utils/format'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import { deleteContent } from '../api/endpoints'
import type { ContentItem } from '../api/endpoints'
import { useAuthStore } from '../stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const props = defineProps<{ content: ContentItem }>()

const emit = defineEmits<{
  (e: 'open', c: ContentItem): void
  (e: 'deleted', id: string): void
}>()

const hasMedia = computed(() => (props.content.media ?? []).length > 0)
const isAuthor = computed(() => props.content.author.id === auth.user?.id)
const kindLabel = computed(() =>
  props.content.kind === 'journal' ? t('timeline.kindJournal') : t('timeline.kindSay')
)
// 列表卡只显天气（现象图标+温度），无则不显
const weatherChip = computed(() => {
  const w = props.content.weather
  if (!w?.city) return ''
  const icon = CONDITION_ICONS[w.condition as WeatherCondition] ?? ''
  const temp = w.tempC !== null && w.tempC !== undefined ? Math.round(w.tempC) + '°C' : ''
  return [icon, temp].filter(Boolean).join(' ')
})

function goEdit() {
  uni.navigateTo({ url: '/pages/detail/detail?id=' + props.content.id + '&edit=1' })
}

async function remove() {
  uni.showModal({
    title: t('common.delete'),
    content: t('detail.deleteConfirm'),
    success: async (res) => {
      if (!res.confirm) return
      try {
        await deleteContent(props.content.id)
        uni.showToast({ title: t('detail.deleted'), icon: 'success' })
        emit('deleted', props.content.id)
      } catch {
        // 错误提示由请求层统一 toast
      }
    }
  })
}
</script>

<style scoped>
.card {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.head {
  display: flex;
  align-items: center;
  gap: 10px;
}
.avatar {
  width: 44px;
  height: 44px;
  border-radius: 50%;
  overflow: hidden;
  background: var(--hz-color-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.avatar-img {
  width: 44px;
  height: 44px;
}
.avatar-fallback {
  color: #fff;
  font-size: 18px;
  font-weight: 700;
}
.head-mid {
  display: flex;
  flex-direction: column;
  gap: 1px;
}
.author {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.kind {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.title {
  font-size: 17px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.meta {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.meta-time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.weather-chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.meta-spacer {
  flex: 1;
}
.foot {
  border-top: 1px solid var(--hz-color-border);
  padding-top: 8px;
}
.foot-item {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
@media (min-width: 768px) {
  .card {
    padding: 20px;
  }
}
</style>
