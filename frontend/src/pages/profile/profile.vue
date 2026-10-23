<template>
  <view class="page">
    <view class="container">
      <ProfileHeader
        :nickname="friend?.nickname ?? nickname"
        :avatar-url="friend?.avatarUrl"
        :signature="friend?.signature"
        :cover-url="friend?.coverUrl"
        :is-self="false"
      />

      <view v-if="pagination.loading.value && !items.length" class="hint">
        <text class="hint-text">{{ $t('common.loading') }}</text>
      </view>
      <view v-else-if="!items.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>

      <view v-for="g in groups" :key="g.key" class="day-group">
        <view class="date-col">
          <text class="date-label">{{ g.label }}</text>
        </view>
        <view class="items-col">
          <view v-for="c in g.items" :key="c.id" class="entry">
            <view class="entry-head">
              <text class="time">{{ formatRelativeTime(c.createdAt) + ' ' }}</text>
            </view>
            <text class="text">{{ c.kind === 'journal' ? c.title : c.text }}</text>
            <MediaGrid v-if="(c.media ?? []).length" :media="c.media" />
            <RecipientsBar :recipients="c.recipients" />
            <view class="entry-foot">
              <!-- 详情页已移除：点击切换内联评论区（同 ContentCard ①A） -->
              <text class="foot-btn" @click.stop="expandedId = expandedId === c.id ? '' : c.id">
                {{ $t('detail.commentCount', { n: c.commentCount }) }}
              </text>
              <view class="head-spacer" />
              <view v-if="weatherChip(c)" class="weather-chip">
                <text class="weather-chip-text">{{ weatherChip(c) }}</text>
              </view>
            </view>
            <!-- 内联评论区：展开才挂载，收起即卸载；评论数变化直接回写该条 -->
            <CardComments
              v-if="expandedId === c.id"
              class="entry-comments"
              :content-id="c.id"
              @count-change="(n) => onCommentCount(c, n)"
            />
          </view>
        </view>
      </view>

      <view v-if="items.length" class="more">
        <text v-if="finished" class="hint-text">{{ $t('common.noMore') }}</text>
        <button v-else class="pick" :disabled="pagination.loading.value" @click="loadMore">
          {{ $t('common.loadMore') }}
        </button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLoad } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import MediaGrid from '../../components/MediaGrid.vue'
import RecipientsBar from '../../components/RecipientsBar.vue'
import ProfileHeader from '../../components/ProfileHeader.vue'
import CardComments from '../../components/CardComments.vue'
import { usePagination } from '../../composables/usePagination'
import { listFriends, listUserContents, type ContentItem, type FriendInfo } from '../../api/endpoints'
import { formatRelativeTime, groupByCnDate } from '../../utils/format'
import { weatherChipText } from '../../utils/weather'

const nickname = ref('')
const userId = ref('')
// 好友资料（签名/封面）来自 GET /friends DTO（moments-wechat 票 01 已下发）
const friend = ref<FriendInfo | null>(null)

const pagination = usePagination<ContentItem>((offset, limit) =>
  listUserContents(userId.value, '', offset, limit)
)
const { items, finished, loadMore } = pagination

// 中国区日期倒序分组，与随记页同款
const groups = computed(() => groupByCnDate(items.value))

// 天气 chip 文案（现象 i18n 回退原文 + 温度 + 城市）
function weatherChip(c: ContentItem) {
  return weatherChipText(c.weather ?? null)
}

// 内联评论区展开态：同一时刻至多展开一条；发送成功后把最新评论数回写该条
const expandedId = ref('')

function onCommentCount(c: ContentItem, n: number) {
  c.commentCount = n
}

// H5 端直接 hash 进入时 onLoad 不触发的问题不存在于 profile（从好友页进入必走 navigateTo），onLoad 足够
onLoad((query) => {
  setNavTitle('profile.title')
  const q = (query ?? {}) as Record<string, string>
  userId.value = q.userId ?? ''
  nickname.value = q.nickname ? decodeURIComponent(q.nickname) : ''
  if (userId.value) {
    pagination.refresh()
    listFriends()
      .then((fs) => {
        friend.value = fs.find((f) => f.id === userId.value) ?? null
      })
      .catch(() => {
        // 拉取失败仅影响头部资料展示，内容流不受影响
      })
  }
})

</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 0 calc(70px + env(safe-area-inset-bottom));
  box-sizing: border-box;
}
.container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.day-group {
  display: flex;
  gap: 12px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
}
.date-col {
  width: 72px;
  flex-shrink: 0;
}
.date-label {
  font-size: 14px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.items-col {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}
.entry {
  border-bottom: 1px solid var(--hz-color-border);
  padding-bottom: 12px;
  cursor: pointer;
}
.entry:last-child {
  border-bottom: none;
  padding-bottom: 0;
}
.entry-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.head-spacer {
  flex: 1;
}
.time {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.text {
  margin-top: 4px;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  white-space: pre-wrap;
  word-break: break-word;
}
.entry-foot {
  margin-top: 6px;
  display: flex;
  align-items: center;
  gap: 8px;
}
.foot-btn {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-interactive);
  cursor: pointer; /* 可点击：展开/收起内联评论区 */
}
/* 内联评论区与正文留一档间距 */
.entry-comments {
  margin-top: 8px;
}
.weather-chip {
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
}
.weather-chip-text {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
}
.more {
  display: flex;
  justify-content: center;
  padding: 4px 0 12px;
}
.hint {
  padding: 40px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}
.hint-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
@media (min-width: 768px) {
  .page {
    padding: 24px 0;
  }
  .container {
    max-width: 48rem;
    margin: 0 auto;
    width: 100%;
  }
  .day-group {
    padding: 20px;
  }
}
</style>
