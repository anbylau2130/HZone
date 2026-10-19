<template>
  <view v-if="images.length" class="grid">
    <view v-for="m in images" :key="m.id" class="cell">
      <image v-if="urls[m.id]" class="img" :src="urls[m.id]" mode="aspectFill" @click="preview" />
      <view v-else class="cell pending" />
    </view>
  </view>
  <view v-if="av" class="av-card" @click="playAv">
    <text class="av-label">{{ av.kind === 'video' ? $t('media.playVideo') : $t('media.playAudio') }}</text>
  </view>
  <text v-if="loadError" class="err">{{ $t('common.mediaLoadFailed') }}</text>
  <video
    v-if="playingVideoUrl"
    class="video-player"
    :src="playingVideoUrl"
    controls
    object-fit="contain"
  />
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { loadMediaUrl } from '../api/media'
import type { MediaItem } from '../api/endpoints'

const props = defineProps<{ media?: MediaItem[] | null }>

const urls = reactive<Record<string, string>>({})
const loadError = ref(false)
const playingVideoUrl = ref('')
const avPlaying = ref(false)
let audioCtx: UniApp.InnerAudioContext | null = null

const images = computed(() => (props.media ?? []).filter((m) => m.kind === 'image'))
const av = computed(() => (props.media ?? []).find((m) => m.kind !== 'image'))

watch(
  () => props.media,
  () => load(),
  { immediate: true }
)

async function load() {
  loadError.value = false
  const targets = [...images.value, ...(av.value ? [av.value] : [])]
  for (const m of targets) {
    if (urls[m.id]) continue
    const path = m.kind === 'image' ? m.thumbUrl || m.url : m.url
    loadMediaUrl(path)
      .then((u) => {
        urls[m.id] = u
      })
      .catch(() => {
        loadError.value = true
      })
  }
}

function preview() {
  const list = images.value.map((m) => urls[m.id]).filter(Boolean)
  if (!list.length) return
  uni.previewImage({ urls: list })
}

async function playAv() {
  if (!av.value) return
  if (avPlaying.value) {
    stopAv()
    return
  }
  try {
    const url = urls[av.value.id] || (await loadMediaUrl(av.value.url))
    if (av.value.kind === 'video') {
      playingVideoUrl.value = url
    } else {
      audioCtx = uni.createInnerAudioContext()
      audioCtx.src = url
      audioCtx.play()
      audioCtx.onEnded(stopAv)
      avPlaying.value = true
    }
  } catch {
    loadError.value = true
  }
}

function stopAv() {
  if (audioCtx) {
    audioCtx.stop()
    audioCtx.destroy()
    audioCtx = null
  }
  avPlaying.value = false
  playingVideoUrl.value = ''
}

onBeforeUnmount(stopAv)
</script>

<style scoped>
.grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 4px;
}
.cell {
  aspect-ratio: 1;
  border-radius: var(--hz-radius-md);
  overflow: hidden;
  background: var(--hz-color-bg);
}
.cell.pending {
  width: 100%;
  height: 100%;
}
.img {
  width: 100%;
  height: 100%;
}
.av-card {
  margin-top: 4px;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
}
.av-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.video-player {
  margin-top: 4px;
  width: 100%;
  height: 220px;
  border-radius: var(--hz-radius-md);
  background: #000;
}
.err {
  font-size: var(--hz-font-size-sm);
  color: #e54d42;
}
</style>
