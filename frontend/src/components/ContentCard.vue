<template>
  <view class="card">
    <!-- 头部一行：头像+作者/类型 … 日期+编辑/删除贴右上角 -->
    <view class="head">
      <AvatarCircle :nickname="content.author.nickname" :avatar-url="content.author.avatarUrl" :size="44" />
      <view class="head-mid">
        <text class="author">{{ content.author.nickname }}</text>
        <text class="kind">{{ kindLabel }}</text>
      </view>
      <view class="head-spacer" />
      <text class="date">{{ formatRelativeTime(content.createdAt) }}</text>
      <CardOwnerActions v-if="isAuthor" @edit="goEdit" @delete="remove" />
    </view>

    <!-- 编辑态：不渲染正文区，原地渲染编辑器（@click.stop 防编辑器内点击冒泡误开详情页） -->
    <view v-if="editing" class="edit-area" @click.stop>
      <!-- 随记：复用 SayComposer 编辑模式，与随记页（moments）原地编辑体验一致 -->
      <SayComposer
        v-if="content.kind === 'say'"
        :edit="content"
        @updated="onSayUpdated"
        @cancel="editing = false"
      />
      <!-- 日志：轻量 Markdown 内联编辑器（cherry 富文本无法内联进卡片，大改仍从详情页进 cherry） -->
      <template v-else>
        <input
          v-model="editTitle"
          class="edit-title"
          :placeholder="$t('publish.titlePlaceholder')"
        />
        <textarea
          v-model="editBody"
          class="edit-body"
          :placeholder="$t('publish.bodyPlaceholder')"
          :maxlength="-1"
          auto-height
        />
        <view class="edit-bar">
          <view class="edit-spacer" />
          <button class="edit-cancel" :disabled="saving" @click="cancelEdit">
            {{ $t('common.cancel') }}
          </button>
          <button class="edit-save" :disabled="saving || !canSave" @click="saveJournal">
            {{ saving ? $t('common.loading') : $t('common.save') }}
          </button>
        </view>
      </template>
    </view>
    <template v-else>
      <text v-if="content.kind === 'journal'" class="title">{{ content.title }}</text>
      <text v-else class="text">{{ content.text }}</text>
      <MediaGrid v-if="hasMedia" :media="content.media" />
      <RecipientsBar :recipients="content.recipients" />
    </template>

    <view class="foot">
      <text class="foot-item">{{ $t('detail.commentCount', { n: content.commentCount }) }}</text>
      <view class="foot-spacer" />
      <text v-if="weatherChip" class="weather-chip">{{ weatherChip }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import MediaGrid from './MediaGrid.vue'
import RecipientsBar from './RecipientsBar.vue'
import CardOwnerActions from './CardOwnerActions.vue'
import AvatarCircle from './AvatarCircle.vue'
import SayComposer from './SayComposer.vue'
import { formatRelativeTime } from '../utils/format'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import { deleteContent, updateContent } from '../api/endpoints'
import type { ContentItem } from '../api/endpoints'
import { useAuthStore } from '../stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const props = defineProps<{ content: ContentItem }>()

const emit = defineEmits<{
  (e: 'deleted', id: string): void
  (e: 'updated', c: ContentItem): void
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

// 原地编辑：卡片内直接改（不跳 publish 页）；大改仍可从详情页进 cherry 编辑器
const editing = ref(false)
const editTitle = ref('')
const editBody = ref('')
const saving = ref(false)

function goEdit() {
  editTitle.value = props.content.title ?? ''
  editBody.value = props.content.markdownBody ?? ''
  editing.value = true
}

function cancelEdit() {
  editing.value = false
}

// 随记原地保存成功：退出编辑态并上抛最新数据，父页面替换数组项
function onSayUpdated(c: ContentItem) {
  editing.value = false
  emit('updated', c)
}

const canSave = computed(
  () => editTitle.value.trim().length > 0 && editBody.value.trim().length > 0
)

async function saveJournal() {
  if (!canSave.value || saving.value) return
  saving.value = true
  try {
    // patch 语义：只传 title/markdownBody，名单/媒体/天气不动
    const updated = await updateContent(props.content.id, {
      title: editTitle.value.trim(),
      markdownBody: editBody.value
    })
    uni.showToast({ title: t('common.saved'), icon: 'success' })
    editing.value = false
    emit('updated', updated)
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    saving.value = false
  }
}

async function remove() {
  uni.showModal({
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
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
.head-mid {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}
.head-spacer {
  flex: 1;
}
.date {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  flex-shrink: 0;
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
  /* 随记正文 16px：比基准大一档（用户反馈要大一点） */
  font-size: 16px;
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.foot {
  display: flex;
  align-items: center;
  gap: 10px;
  border-top: 1px solid var(--hz-color-border);
  padding-top: 8px;
}
.foot-item {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
}
.foot-spacer {
  flex: 1;
}
.weather-chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 原地编辑区：复用卡片内边距，浅色框与 SayComposer 同风格 */
.edit-area {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.edit-title {
  height: 36px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: 16px;
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.edit-body {
  width: 100%;
  box-sizing: border-box;
  min-height: 120px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 8px 12px;
  font-size: 16px;
  line-height: 1.6;
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.edit-bar {
  display: flex;
  align-items: center;
  gap: 6px;
}
.edit-spacer {
  flex: 1;
}
.edit-save {
  height: 34px;
  line-height: 34px;
  padding: 0 20px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.edit-save[disabled] {
  opacity: 0.55;
}
.edit-cancel {
  height: 34px;
  line-height: 34px;
  padding: 0 16px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-bg);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
}
@media (min-width: 768px) {
  .card {
    padding: 20px;
  }
}
</style>
