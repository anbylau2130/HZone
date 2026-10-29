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
        <view class="pa-lib-actions">
          <!-- #ifdef H5 -->
          <!-- 文件夹批量导入：webkitdirectory 选整个目录，过滤音频逐个上传+入列 -->
          <view class="pa-upload" @click="pickFolder">
            <text class="pa-upload-text">📁 {{ importing ? $t('player.importing', { x: importing.done, n: importing.total }) : $t('player.importFolder') }}</text>
          </view>
          <view class="pa-upload" @click="pickAudio"><text class="pa-upload-text">＋ {{ uploading ? $t('common.loading') : $t('player.upload') }}</text></view>
          <!-- #endif -->
        </view>
      </view>
      <scroll-view scroll-y class="pa-lib-list">
        <view v-if="!libItems.length" class="pa-empty">
          <text class="pa-empty-text">{{ $t('player.libraryEmpty') }}</text>
        </view>
        <view v-for="a in libItems" :key="a.id" class="pa-lib-row">
          <view class="pa-lib-main" @click="addFromLibrary(a)">
            <text class="pa-lib-name">{{ a.originalName }}</text>
            <text v-if="libMeta(a)" class="pa-lib-meta">{{ libMeta(a) }}</text>
          </view>
          <text class="pa-lib-rename" :title="$t('common.rename')" @click.stop="renameAsset(a)">✎</text>
        </view>
      </scroll-view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { usePlayerStore } from '../stores/player'
import { listMyAudioAssets, renameMediaAsset, type AudioAssetInfo } from '../api/endpoints'
import { uploadFile } from '../api/client'
import { probeAudio, fmtSize, fmtDuration } from '../utils/audioMeta'
import { i18n } from '../locale'

const { t } = useI18n()

const player = usePlayerStore()
const emit = defineEmits<{ (e: 'added'): void }>()

const libOpen = ref(false)
const urlOpen = ref(false)
const libItems = ref<AudioAssetInfo[]>([])
const urlTitle = ref('')
const urlAddr = ref('')
const uploading = ref(false)
/** 文件夹导入进度（null = 空闲）；done/total 用于按钮文案实时反馈。 */
const importing = ref<{ done: number; total: number } | null>(null)

/** 目录选择器放行的音频：type 前缀（部分浏览器给得出）+ 扩展名兜底（目录选择时常见空 type）。 */
const AUDIO_EXT = /\.(mp3|m4a|aac|wav|flac|ogg|oga|opus)$/i

function isAudioFile(f: File): boolean {
  return f.type.startsWith('audio/') || AUDIO_EXT.test(f.name)
}

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

/** 库行元信息：艺术家 · 时长 · 大小（探测到的才显示）；共享曲带 🌐 标。 */
function libMeta(a: AudioAssetInfo): string {
  const parts = [a.artist, fmtDuration(a.durationSeconds), fmtSize(a.sizeBytes)].filter(Boolean)
  return (a.isShared ? '🌐 ' : '') + parts.join(' · ')
}

/** 上传附加字段：探测到的音频元数据（空值不上送，服务端存 null）。 */
async function metaFields(file: File): Promise<Record<string, string>> {
  try {
    const meta = await probeAudio(file)
    const fields: Record<string, string> = {}
    if (meta.artist) fields.artist = meta.artist
    if (meta.durationSeconds) fields.durationSeconds = String(Math.round(meta.durationSeconds * 10) / 10)
    if (meta.bitrateKbps) fields.bitrateKbps = String(meta.bitrateKbps)
    return fields
  } catch {
    return {} // 探测失败不阻断上传
  }
}

async function addFromLibrary(a: AudioAssetInfo) {
  try {
    await player.addMedia(a.id)
    emit('added')
  } catch {
    // 请求层已 toast
  }
}

/** 资产重命名：uni.showModal 可编辑弹窗（H5/App 同 API），改完刷新库列表。 */
function renameAsset(a: AudioAssetInfo) {
  uni.showModal({
    title: t('common.rename'),
    editable: true,
    placeholderText: a.originalName,
    content: a.originalName,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (res) => {
      const name = (res.content ?? '').trim()
      if (!res.confirm || !name || name === a.originalName) return
      try {
        await renameMediaAsset(a.id, name)
        a.originalName = name
      } catch {
        // 请求层已 toast（校验错误）
      }
    }
  })
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

// H5 上传：原生 input（accept 音频）；选中后先弹命名框（预填去扩展名的
// 原名，确认后以该名入库——不然上传后查找困难）；取消/留空按原名继续传
function pickAudio() {
  // #ifdef H5
  if (uploading.value) return
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = 'audio/*'
  input.onchange = () => {
    const file = input.files?.[0]
    if (file) void nameThenUpload(file)
  }
  input.click()
  // #endif
}

/** 命名后上传：预填原名去扩展名；确认→改名上传；取消/空→原名直传（不阻断）。
    命名后再选共享/私有（音乐目录体系：私有 → music/{我}/，共享 → music/ 根）。 */
async function nameThenUpload(file: File) {
  const baseName = file.name.replace(/\.[^.]+$/, '')
  const ext = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')) : ''
  const named = await new Promise<string | null>((resolve) => {
    uni.showModal({
      title: t('player.nameAudio'),
      editable: true,
      placeholderText: baseName,
      content: baseName,
      confirmText: t('common.confirm'),
      cancelText: t('player.uploadAsIs'),
      success: (res) => resolve(res.confirm && res.content?.trim() ? res.content.trim() : null),
      fail: () => resolve(null)
    })
  })
  const shared = await new Promise<boolean>((resolve) => {
    uni.showActionSheet({
      itemList: [t('musiclib.privateScope'), t('musiclib.sharedScope')],
      success: (res) => resolve(res.tapIndex === 1),
      fail: () => resolve(false)
    })
  })
  const finalFile = named ? new File([file], named + ext, { type: file.type }) : file
  await uploadAndAdd(finalFile, shared)
}

async function uploadAndAdd(file: File, shared = false) {
  uploading.value = true
  try {
    const fields = await metaFields(file)
    fields.shared = shared ? 'true' : 'false'
    const asset = await uploadFile<{ id: string }>('/api/media', file, fields)
    await player.addMedia(asset.id)
    libItems.value = (await listMyAudioAssets()).items
    emit('added')
  } catch {
    // 请求层已 toast
  } finally {
    uploading.value = false
  }
}

// 文件夹导入：webkitdirectory 整目录选择 → 过滤音频 → 逐个上传+入列；
// 单文件失败不中断批次（超限/格式等按跳过计），列表打满（409）提前收工
function pickFolder() {
  if (importing.value || uploading.value) return
  const input = document.createElement('input')
  input.type = 'file'
  ;(input as HTMLInputElement & { webkitdirectory: boolean }).webkitdirectory = true
  input.onchange = () => {
    const files = [...(input.files ?? [])]
    if (files.length) void importFolder(files)
  }
  input.click()
}

async function importFolder(files: File[]) {
  const audio = files.filter(isAudioFile)
  const skipped = files.length - audio.length
  if (!audio.length) {
    uni.showToast({ title: i18n.global.t('player.importNoAudio'), icon: 'none' })
    return
  }
  importing.value = { done: 0, total: audio.length }
  let ok = 0
  let fail = 0
  for (const f of audio) {
    try {
      const fields = await metaFields(f)
      const asset = await uploadFile<{ id: string }>('/api/media', f, fields)
      await player.addMedia(asset.id)
      ok++
    } catch (err) {
      fail++
      if ((err as { status?: number }).status === 409) break // 播放列表打满：余下不再尝试
    }
    importing.value = { done: importing.value.done + 1, total: audio.length }
  }
  importing.value = null
  try {
    libItems.value = (await listMyAudioAssets()).items
  } catch {
    // 库刷新失败不影响导入结果反馈
  }
  emit('added')
  uni.showToast({
    title: i18n.global.t('player.importDone', { ok, skip: skipped + fail }),
    icon: 'none'
  })
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
  gap: 6px;
}
.pa-lib-actions {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
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
.pa-lib-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.pa-lib-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  cursor: pointer;
}
.pa-lib-meta {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.pa-lib-rename {
  flex-shrink: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 0 4px;
  cursor: pointer;
}
.pa-lib-rename:hover {
  color: var(--hz-color-interactive);
}
</style>
