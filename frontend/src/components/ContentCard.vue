<template>
  <view class="card">
    <!-- 头部一行：头像+作者/类型 … 日期时间贴右上角（用户要求：绝对日期+时间） -->
    <view class="head">
      <AvatarCircle :nickname="content.author.nickname" :avatar-url="content.author.avatarUrl" :size="44" />
      <view class="head-mid">
        <text class="author">{{ content.author.nickname }}</text>
        <view class="kind-row">
          <text class="kind">{{ kindLabel }}</text>
          <!-- 心情（随记标记了才有）：跟在类型后面 -->
          <text v-if="moodEmoji" class="kind-mood">{{ moodEmoji }}</text>
        </view>
      </view>
      <view class="head-spacer" />
      <!-- 右上角两行：日期时间 + 天气·位置（右对齐；任一缺失只显存在的部分） -->
      <view class="head-right">
        <text class="head-date">{{ formatDateTime(content.createdAt) }}</text>
        <text v-if="weatherLine" class="head-weather">{{ weatherLine }}</text>
      </view>
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
      <!-- 标签 chips：正文下方横排（点 chip 即跳笔记页按标签筛选） -->
      <view v-if="(content.tags?.length ?? 0) > 0" class="tags-line">
        <text
          v-for="tag in content.tags"
          :key="tag"
          class="tag-chip"
          @click.stop="goTag(tag)"
        ># {{ tag }}</text>
      </view>
    </template>

    <!-- 内联评论区（①A）：详情页已移除，评论在卡片内展开；发送期间由 CardComments 防重复 -->
    <CardComments
      v-if="showComments && !editing"
      :content-id="content.id"
      @count-change="onCommentCountChange"
    />

    <view class="foot">
      <!-- 点击「评论 n」展开/收起内联评论区（@click.stop 防冒泡） -->
      <text class="foot-item" @click.stop="showComments = !showComments">{{
        $t('detail.commentCount', { n: commentCount })
      }}</text>
      <!-- 点赞切换：已赞主色高亮（@click.stop 防冒泡到卡片跳转） -->
      <view class="foot-like" :class="{ on: likedByMe }" @click.stop="onToggleLike">
        <svg class="like-ico" :class="{ pop: likePopped }" viewBox="0 0 24 24" :fill="likedByMe ? 'currentColor' : 'none'" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <path d="M14 9V5a3 3 0 0 0-3-3l-4 9v11h11.28a2 2 0 0 0 2-1.7l1.38-9a2 2 0 0 0-2-2.3zM7 22H4a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2h3" />
        </svg>
        <text class="like-count">{{ likeCount }}</text>
      </view>
      <view class="foot-spacer" />
      <!-- 编辑/删除贴评论行最右（用户要求，原在右上角）；分享 🔗 同组 -->
      <CardOwnerActions v-if="isAuthor" @edit="goEdit" @delete="remove" @share="shareOpen = true" />
    </view>

    <!-- 分享弹层（共享组件 ShareSheet） -->
    <ShareSheet v-model:visible="shareOpen" :content-id="content.id" />
  </view>
</template>

<script setup lang="ts">
import { computed, defineAsyncComponent, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import MediaGrid from './MediaGrid.vue'
import RecipientsBar from './RecipientsBar.vue'
import CardOwnerActions from './CardOwnerActions.vue'
import ShareSheet from './ShareSheet.vue'
import AvatarCircle from './AvatarCircle.vue'
import CardComments from './CardComments.vue'
import SayComposer from './SayComposer.vue'
import { formatDateTime } from '../utils/format'
import { CONDITION_ICONS, type WeatherCondition } from '../utils/weather'
import { deleteContent, updateContent, toggleLike } from '../api/endpoints'
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
/** 心情档位 → emoji（未标/笔记为 null 不渲染）。 */
const moodEmoji = computed(() =>
  props.content.moodLevel ? MOOD_EMOJI[props.content.moodLevel] ?? null : null
)
const MOOD_EMOJI: Record<number, string> = { 1: '😢', 2: '😕', 3: '😐', 4: '🙂', 5: '😄' }
// ---------- 内联评论（①A）：点击「评论 n」展开/收起，收起即卸载 CardComments ----------
const showComments = ref(false)
// 本地维护评论数显示：列表数据 + 发送成功后 CardComments 上抛的新值
const commentCount = ref(props.content.commentCount)

function onCommentCountChange(n: number) {
  commentCount.value = n
}

// ---------- 点赞：乐观更新 + 失败回滚；图标弹一下给即时反馈 ----------
const likeCount = ref(props.content.likeCount ?? 0)
const likedByMe = ref(props.content.likedByMe ?? false)
const likePopped = ref(false)
const likeBusy = ref(false)

async function onToggleLike() {
  if (likeBusy.value) return
  likeBusy.value = true
  // 乐观翻转（点赞要跟手，不能等网络）
  likedByMe.value = !likedByMe.value
  likeCount.value += likedByMe.value ? 1 : -1
  likePopped.value = likedByMe.value
  try {
    const res = await toggleLike(props.content.id)
    likedByMe.value = res.liked
    likeCount.value = res.likeCount
  } catch {
    // 失败回滚（提示由请求层统一 toast）
    likedByMe.value = !likedByMe.value
    likeCount.value += likedByMe.value ? 1 : -1
  } finally {
    likeBusy.value = false
    if (!likedByMe.value) likePopped.value = false
  }
}

// 右上角第二行：天气（现象图标+温度）· 位置（城市）——不再依赖城市存在
//（旧实现无城市连天气一起藏，用户反馈「天气没显示」的根因）
const weatherLine = computed(() => {
  const w = props.content.weather
  if (!w || (!w.condition && w.tempC == null && !w.city)) return ''
  const icon = w.condition ? CONDITION_ICONS[w.condition as WeatherCondition] ?? '' : ''
  const temp = w.tempC !== null && w.tempC !== undefined ? `${Math.round(w.tempC)}°C` : ''
  const weather = [icon, temp].filter(Boolean).join(' ')
  return [weather, w.city].filter(Boolean).join(' · ')
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

/** 点标签 chip → 笔记页按标签筛选（tab 页间用 redirectTo——与自绘 AppTabBar 同款；
 *  switchTab 需原生 tabBar 配置，本项目自绘无配置，H5 下静默失败） */
function goTag(tag: string) {
  uni.setStorageSync('hz-journals-tag', tag)
  uni.redirectTo({ url: '/pages/journals/journals' })
}

// ---------- 只读分享外链：弹层抽为共享组件 ShareSheet（随记页卡片同款） ----------
const shareOpen = ref(false)

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
    // 多编辑面草稿一致性：此处保存后，发布页遗留的同 ID 草稿即过期（restoreDraft 会
    // 按时间戳丢弃），这里顺手清掉更干净
    try { localStorage.removeItem('hz-draft-journal-e-' + props.content.id) } catch {}
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
/* 站内信跳转定位：focusContent.ts 加 .focus-flash 闪两下高亮 */
.card.focus-flash {
  animation: hz-focus-flash 1.1s ease-in-out 2;
}
@keyframes hz-focus-flash {
  0%, 100% {
    box-shadow: none;
  }
  50% {
    box-shadow: 0 0 0 3px var(--hz-color-primary);
  }
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
/* 右上角信息列：第一行日期时间，第二行天气·位置（右对齐，小字号 secondary） */
.head-right {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 1px;
  flex-shrink: 0;
}
.head-date {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
.head-weather {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  white-space: nowrap;
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
.kind-row {
  display: flex;
  align-items: center;
  gap: 4px;
}
.kind-mood {
  font-size: var(--hz-font-size-sm);
  line-height: 1;
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
  cursor: pointer;
}
/* 标签 chips 行：正文下方横排 */
.tags-line {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
  margin-top: 2px;
}
/* 点赞：评论行内按钮，已赞主色 + 图标弹跳动画 */
.foot-like {
  display: flex;
  align-items: center;
  gap: 4px;
  color: var(--hz-color-text-secondary);
  cursor: pointer;
  user-select: none;
}
.foot-like.on {
  color: var(--hz-color-primary);
}
.like-ico {
  width: 16px;
  height: 16px;
}
.like-ico.pop {
  animation: like-pop 0.3s ease;
}
@keyframes like-pop {
  0% { transform: scale(1); }
  45% { transform: scale(1.35); }
  100% { transform: scale(1); }
}
.like-count {
  font-size: var(--hz-font-size-sm);
  font-variant-numeric: tabular-nums;
}
.tag-chip {
  padding: 2px 9px;
  border-radius: 999px;
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
  cursor: pointer;
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
/* 内联编辑器与发布页同款贴边：去圆角/边框，高度上限保证保存条在首屏可见 */
.edit-area :deep(.cherry) {
  border: none;
  border-radius: 0;
  max-height: 48vh;
}
.edit-area :deep(.cherry-host) {
  max-height: 48vh;
  overflow: hidden;
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
  /* 编辑长文时保存/取消始终可见（钉在编辑区底部） */
  position: sticky;
  bottom: 0;
  z-index: 5;
  background: var(--hz-color-surface);
  padding: 6px 0 2px;
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
