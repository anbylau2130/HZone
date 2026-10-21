<template>
  <view class="ph">
    <!-- 封面区：无封面 = 主题 primary 渐变；有封面 = 授权 blob 真图。仅本人可点上传。 -->
    <view class="cover-zone" :class="{ pickable: isSelf }" @click="onCoverClick">
      <image v-if="coverSrc" class="cover-img" :src="coverSrc" mode="aspectFill" />
      <view v-else class="cover-fallback" />
    </view>
    <!-- 头像压角：下缘越出封面 24px（微信同款） -->
    <view class="avatar-abs">
      <AvatarCircle :nickname="nickname" :avatar-url="avatarUrl" :size="72" />
    </view>
    <!-- 昵称与头像下段并排，签名档在其下右对齐 -->
    <view class="meta">
      <text class="nick">{{ nickname }}</text>
      <text v-if="signature" class="sig">{{ signature }}</text>
      <text v-else-if="isSelf" class="sig muted">{{ $t('moments.noSignature') }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import AvatarCircle from './AvatarCircle.vue'
import { loadMediaUrl } from '../api/media'

const props = withDefaults(
  defineProps<{
    nickname: string
    avatarUrl?: string | null
    signature?: string | null
    coverUrl?: string | null
    /** 是否本人主页：仅本人点击封面上传。 */
    isSelf?: boolean
  }>(),
  { isSelf: false }
)

const emit = defineEmits<{ (e: 'cover-pick'): void }>()

const coverSrc = ref<string | null>(null)

watch(
  () => props.coverUrl,
  async (url) => {
    if (!url) {
      coverSrc.value = null
      return
    }
    try {
      coverSrc.value = await loadMediaUrl(url)
    } catch {
      // 拉取失败回退默认渐变
      coverSrc.value = null
    }
  },
  { immediate: true }
)

function onCoverClick() {
  if (props.isSelf) emit('cover-pick')
}
</script>

<style scoped>
.ph {
  position: relative;
}

.cover-zone {
  height: 300px;
  width: 100%;
  overflow: hidden;
}

/* 无封面默认：主题 primary 暖色渐变 */
.cover-fallback {
  width: 100%;
  height: 100%;
  background: linear-gradient(135deg, var(--hz-color-primary) 0%, #f5c98a 60%, #f7dcb4 100%);
}

.cover-img {
  width: 100%;
  height: 100%;
}

.cover-zone.pickable {
  cursor: pointer;
}

.avatar-abs {
  position: absolute;
  right: 16px;
  top: 300px;
  margin-top: -48px; /* 72px 头像，下缘越出封面 24px */
  border-radius: 50%;
  box-shadow: 0 0 0 2px var(--hz-color-surface);
}

/* 昵称与头像越出段并排（高 48px），签名档在其下右对齐 */
.meta {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  min-height: 48px;
  justify-content: flex-end;
  padding-right: 104px; /* 头像 72 + 右边距 16 + 间距 16 */
}

.nick {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}

.sig {
  margin-top: 6px;
  font-size: 13px;
  color: var(--hz-color-text-secondary);
  text-align: right;
}

.sig.muted {
  opacity: 0.7;
}
</style>
