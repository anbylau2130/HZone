<template>
  <view class="page">
    <view class="card">
      <text class="title">{{ $t('admin.title') }}</text>

      <!-- 服务器音乐目录（player.musicDir）：目录内音频作为全员播放器默认曲库 -->
      <view class="section">
        <text class="sec-title">{{ $t('admin.musicDir.title') }}</text>
        <text class="sec-hint">{{ $t('admin.musicDir.hint') }}</text>
        <view v-if="currentDir" class="dir-line">
          <text class="dir-text">{{ currentDir }}</text>
          <text class="dir-count">{{ $t('admin.musicDir.count', { n: fileCount }) }}</text>
        </view>
        <input v-model="dirInput" class="dir-field" :placeholder="$t('admin.musicDir.placeholder')" />
        <view class="dir-actions">
          <button v-if="currentDir" class="mini ghost" @click="clearDir">{{ $t('admin.musicDir.clear') }}</button>
          <button class="mini" :disabled="saving || !dirInput.trim()" @click="saveDir">
            {{ saving ? $t('common.loading') : $t('common.save') }}
          </button>
        </view>
        <text v-if="saveError" class="dir-error">{{ saveError }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import { useI18n } from 'vue-i18n'
import { getServerMusicLibrary, setServerMusicDir } from '../../api/endpoints'
import { usePlayerStore } from '../../stores/player'

const { t } = useI18n()

onShow(() => {
  setNavTitle('admin.title')
  void refresh()
})

const currentDir = ref<string | null>(null)
const fileCount = ref(0)
const dirInput = ref('')
const saving = ref(false)
const saveError = ref('')

async function refresh() {
  try {
    const lib = await getServerMusicLibrary()
    currentDir.value = lib.musicDir
    fileCount.value = lib.files.length
    dirInput.value = lib.musicDir ?? ''
    saveError.value = ''
  } catch {
    // 请求层已 toast（非 admin 403 提示由后端 ProblemDetails 提供）
  }
}

async function saveDir() {
  const dir = dirInput.value.trim()
  if (!dir || saving.value) return
  saving.value = true
  saveError.value = ''
  try {
    await setServerMusicDir(dir)
    // 保存成功：曲库立即生效（播放器面板同步刷新）
    await usePlayerStore().reloadServerLibrary()
    await refresh()
  } catch (err) {
    // 目录不存在等校验错误：显示字段级错误
    saveError.value = (err as { data?: { errors?: Record<string, string[]> } }).data?.errors?.musicDir?.[0] ?? t('common.requestFailed')
  } finally {
    saving.value = false
  }
}

async function clearDir() {
  if (saving.value) return
  saving.value = true
  saveError.value = ''
  try {
    await setServerMusicDir('')
    await usePlayerStore().reloadServerLibrary()
    await refresh()
  } catch {
    // 请求层已 toast
  } finally {
    saving.value = false
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  box-sizing: border-box;
}
.card {
  width: 100%;
  max-width: 480px;
  margin-top: 32px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.title {
  font-size: 20px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.sec-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.sec-hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  line-height: 1.5;
}
.dir-line {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
}
.dir-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  word-break: break-all;
}
.dir-count {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
}
.dir-field {
  height: 38px;
  padding: 0 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.dir-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}
.mini {
  height: 34px;
  line-height: 34px;
  padding: 0 18px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.dir-error {
  font-size: var(--hz-font-size-sm);
  color: #e54d42;
}
</style>
