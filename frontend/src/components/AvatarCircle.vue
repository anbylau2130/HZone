<template>
  <view class="ac" :style="{ width: size + 'px', height: size + 'px' }">
    <!-- 票 05：有头像走授权 blob 拉取的真实图，否则昵称首字占位 -->
    <image v-if="src" class="ac-img" :src="src" mode="aspectFill" />
    <text v-else class="ac-initial" :style="{ fontSize: Math.round(size * 0.42) + 'px' }">
      {{ initial }}
    </text>
  </view>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { fetchAvatarBlobUrl } from '../api/media'

const props = withDefaults(
  defineProps<{
    nickname: string
    avatarUrl?: string | null
    /** 直径（px）。 */
    size?: number
  }>(),
  { size: 40 }
)

const src = ref<string | null>(null)

watch(
  () => props.avatarUrl,
  async (url) => {
    if (!url) {
      src.value = null
      return
    }
    try {
      src.value = await fetchAvatarBlobUrl(url)
    } catch {
      // 拉取失败回退首字占位
      src.value = null
    }
  },
  { immediate: true }
)

const initial = computed(() => props.nickname.slice(0, 1))
</script>

<style scoped>
.ac {
  border-radius: 50%;
  overflow: hidden;
  background: var(--hz-color-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.ac-img {
  width: 100%;
  height: 100%;
}
.ac-initial {
  color: #fff;
  font-weight: 700;
}
</style>
