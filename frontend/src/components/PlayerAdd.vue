<template>
  <!-- 播放列表添加块（共享）：音频库（含 H5 上传）+ 外链表单。
       桌面右栏 PlayerPanel 与移动端 MiniPlayer 抽屉共用。 -->
  <view class="pa">
    <view class="pa-row">
      <view class="pa-btn" @click="toggleLib"><text class="pa-text">{{ $t('player.addFromLibrary') }}</text></view>
      <view class="pa-btn" @click="urlOpen = !urlOpen"><text class="pa-text">{{ $t('player.addLink') }}</text></view>
    </view>

    <view v-if="urlOpen" class="pa-url">
      <input v-model="urlTitle" class="pa-field" :placeholder="$t('player.linkTitle')" />
      <input v-model="urlAddr" class="pa-field" :placeholder="$t('player.linkUrl')" />
      <view class="pa-actions">
        <button class="pa-mini ghost" @click="urlOpen = false">{{ $t('common.cancel') }}</button>
        <button class="pa-mini" :disabled="!urlTitle.trim() || !urlAddr.trim()" @click="submitUrl">
          {{ $t('common.confirm') }}
        </button>
      </view>
    </view>

    <view v-if="libOpen" class="pa-lib">
      <view class="pa-lib-head">
        <text class="pa-lib-title">{{ $t('player.library') }}</text>
        <!-- #ifdef H5 -->
        <view class="pa-upload" @click="pickAudio"><text class="pa-upload-text">＋ {{ uploading ? $t('common.loading') : $t('player.upload') }}</text></view>
        <!-- #endif -->
      </view>
      <scroll-view scroll-y class="pa-lib-list">
        <view v-if="!libItems.length" class="pa-empty">
          <text class="pa-empty-text">{{ $t('player.libraryEmpty') }}</text>
        </view>
        <view v-for="a in libItems" :key="a.id" class="pa-lib-row" @click="addFromLibrary(a)">
          <text class="pa-lib-name">{{ a.originalName }}</text>
          <text class="pa-lib-size">{{ fmtSize(a.sizeBytes) }}</text>
        </view>
      </scroll-view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { usePlayerStore } from '../stores/player'
import { listMyAudioAssets, type AudioAssetInfo } from '../api/endpoints'
import { uploadFile } from '../api/client'

const player = usePlayerStore()
const emit = defineEmits<{ (e: 'added'): void }>()

const libOpen = ref(false)
const urlOpen = ref(false)
const libItems = ref<AudioAssetInfo[]>([])
const urlTitle = ref('')
const urlAddr = ref('')
const uploading = ref(false)

async function openLib() {
  libOpen.value = true
  try {
    libItems.value = (await listMyAudioAssets()).items
  } catch {
    libItems.value = []
  }
}

function toggleLib() {
  if (libOpen.value) libOpen.value = false
  else void openLib()
}

function fmtSize(n: number): string {
  return n >= 1048576 ? `${(n / 1048576).toFixed(1)}M` : `${Math.max(1, Math.round(n / 1024))}K`
}

async function addFromLibrary(a: AudioAssetInfo) {
  try {
    await player.addMedia(a.id)
    emit('added')
  } catch {
    // 请求层已 toast
  }
}

async function submitUrl() {
  try {
    await player.addUrl(urlTitle.value.trim(), urlAddr.value.trim())
    urlTitle.value = ''
    urlAddr.value = ''
    urlOpen.value = false
    emit('added')
  } catch {
    // 请求层已 toast（校验错误信息由 ProblemDetails 提取）
  }
}

// H5 上传：原生 input（accept 音频），上传后自动加入播放列表并刷新库
function pickAudio() {
  // #ifdef H5
  if (uploading.value) return
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'audio/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) void uploadAndAdd(file)
  }
  input.click()
  // #endif
}

async function uploadAndAdd(file: File) {
  uploading.value = true
  try {
    const asset = await uploadFile<{ id: string }>('/api/media', file)
    await player.addMedia(asset.id)
    libItems.value = (await listMyAudioAssets()).items
    emit('added')
  } catch {
    // 请求层已 toast
  } finally {
    uploading.value = false
  }
}
</script>

<style scoped>
.pa {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.pa-row {
  display: flex;
  gap: 8px;
}
.pa-btn {
  flex: 1;
  height: 32px;
  border: 1px dashed var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}
.pa-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.pa-url {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.pa-field {
  height: 34px;
  padding: 0 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-surface);
}
.pa-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.pa-mini {
  height: 30px;
  line-height: 30px;
  padding: 0 16px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.pa-mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.pa-lib {
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.pa-lib-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.pa-lib-title {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text);
}
.pa-upload {
  padding: 2px 10px;
  border: 1px dashed var(--hz-color-primary);
  border-radius: 999px;
  cursor: pointer;
}
.pa-upload-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.pa-lib-list {
  max-height: 180px;
}
.pa-empty {
  padding: 12px 0;
  display: flex;
  justify-content: center;
}
.pa-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.pa-lib-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 7px 6px;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.pa-lib-row:hover {
  background: var(--hz-color-bg);
}
.pa-lib-name {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pa-lib-size {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
</style>
