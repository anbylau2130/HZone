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
      <!-- 日志：cherry 精简内联编辑（compact） -->
      <template v-else>
        <input
          v-model="editTitle"
          class="edit-title"
          :placeholder="$t('publish.titlePlaceholder')"
        />
        <!-- 日志：cherry 精简内联编辑（compact），与发布页同一渲染引擎保证观感一致；
             defineAsyncComponent 懒加载，只有进编辑态才拉取 cherry chunk（不进主包） -->
        <AsyncCherryEditor
          v-model="editBody"
          preview
          compact
          height="240px"
          :placeholder="$t('publish.bodyPlaceholder')"
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
      <!-- 日志正文预览：读侧统一 cherry previewOnly（用户决策），与编辑器渲染完全一致；
           IntersectionObserver 懒挂载——列表一页 15+ 卡，滚进视口才拉取 cherry chunk 并渲染，
           挂载前 .md-preview 以 min-height 占位防跳变（用 div：view 的 ref 拿不到 DOM 元素） -->
      <div
        v-if="content.kind === 'journal' && (content.markdownBody ?? '').trim()"
        ref="previewHost"
        class="md-preview"
      >
        <AsyncCherryEditor v-if="bodyVisible" :model-value="content.markdownBody ?? ''" preview-only />
      </div>
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
import { computed, defineAsyncComponent, onBeforeUnmount, ref, watch } from 'vue'
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

// cherry 编辑器懒加载：模块顶层异步组件，vite 自动拆 chunk（只有进编辑态才加载）
const AsyncCherryEditor = defineAsyncComponent(() => import('./CherryEditor.vue'))

// ---------- 日志正文预览懒挂载 ----------
// cherry 实例重（列表一页 15+ 卡不能每张都挂）：滚进视口才挂 AsyncCherryEditor(previewOnly)，
// 已挂载的保持不卸载（滚出视口仍保留，避免回滚重渲染）；进入原地编辑态时预览区随 v-if
// 卸载，退出后 watch(previewHost) 重新观察
const bodyVisible = ref(false)
const previewHost = ref<HTMLElement | null>(null)
let io: IntersectionObserver | null = null

function observePreview() {
  io?.disconnect()
  io = null
  if (bodyVisible.value || !previewHost.value) return
  io = new IntersectionObserver(
    (entries) => {
      if (entries.some((e) => e.isIntersecting)) {
        bodyVisible.value = true
        io?.disconnect()
        io = null
      }
    },
    { rootMargin: '200px' } // 提前半屏开始渲染，滚动到位时正文已就绪
  )
  io.observe(previewHost.value)
}
watch(previewHost, () => observePreview())
onBeforeUnmount(() => io?.disconnect())

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

// ---------- 日志原地编辑：cherry 精简内联编辑（compact） ----------
// 正文走 AsyncCherryEditor v-model 双向绑定直接驱动 editBody，无需额外的 input 回调桥接
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
    // patch 语义：只传 title/markdownBody，名单/媒体/天气不动；正文以编辑器当前值为准
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
  /* 可选中复制（uni-app H5 默认禁选中，moments 同款修法） */
  cursor: auto;
  user-select: text;
  -webkit-user-select: text;
}
.text {
  /* 随记正文 16px：比基准大一档（用户反馈要大一点）；可选中复制 */
  font-size: 16px;
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
  cursor: auto;
  user-select: text;
  -webkit-user-select: text;
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
/* 日志正文预览：cherry previewOnly 懒挂载前的占位（防列表跳变） */
.md-preview {
  min-height: 60px;
}
/* 列表卡紧凑字号/行距（此前 .md-body 时代同款）；顶层 :deep——uni 会把嵌套 :deep
   编译成落错层的 data-v 规则（MarkdownBody 顶部注释教训），一律写顶层 */
.md-preview :deep(.cherry-previewer) {
  font-size: var(--hz-font-size-sm);
  line-height: 1.6;
}
.md-preview :deep(.cherry-markdown > :first-child) {
  margin-top: 0;
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
