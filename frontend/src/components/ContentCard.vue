<template>
  <view class="card" @click="$emit('open', content)">
    <view class="head">
      <text class="author">{{ content.author.nickname }}</text>
      <text class="kind">{{ kindLabel }}</text>
      <text class="time">{{ formatDateTime(content.createdAt) }}</text>
    </view>
    <text v-if="content.kind === 'journal'" class="title">{{ content.title }}</text>
    <text v-else class="text">{{ content.text }}</text>
    <MediaGrid v-if="hasMedia" :media="content.media" />
    <RecipientsBar :recipients="content.recipients" />
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
import { formatDateTime } from '../utils/format'
import type { ContentItem } from '../api/endpoints'

const { t } = useI18n()
const props = defineProps<{ content: ContentItem }>()

defineEmits<{ (e: 'open', c: ContentItem): void }>()

const hasMedia = computed(() => (props.content.media ?? []).length > 0)
const kindLabel = computed(() =>
  props.content.kind === 'journal' ? t('timeline.kindJournal') : t('timeline.kindSay')
)
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
  align-items: baseline;
  gap: 8px;
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
.time {
  margin-left: auto;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
.foot {
  display: flex;
  gap: 12px;
}
.foot-item {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
