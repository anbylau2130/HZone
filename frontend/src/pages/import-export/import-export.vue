<template>
  <view class="page">
    <view class="section">
      <text class="section-title">{{ $t('ie.exportSection') }}</text>
      <text class="hint">{{ $t('ie.exportDesc') }}</text>
      <button class="btn" :disabled="exporting" @click="doExport">
        {{ exporting ? $t('common.loading') : $t('ie.exportBtn') }}
      </button>
    </view>

    <view class="section">
      <text class="section-title">{{ $t('ie.importSection') }}</text>
      <text class="hint">{{ $t('ie.importDesc') }}</text>
      <button class="btn ghost" :disabled="importing" @click="doImport">
        {{ importing ? $t('common.loading') : $t('ie.importBtn') }}
      </button>
    </view>

    <view class="section">
      <text class="section-title">{{ $t('ie.batchSection') }}</text>
      <text class="hint">{{ $t('ie.batchDesc') }}</text>
      <view v-if="!contents.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>
      <view v-for="c in contents" :key="c.id" class="row" @click="toggleContent(c.id)">
        <view class="row-main">
          <text class="row-title">{{ c.kind === 'journal' ? c.title : c.text }}</text>
          <text class="hint">{{ formatDateTime(c.createdAt) }}</text>
        </view>
        <view class="check" :class="{ on: selectedIds.includes(c.id) }" />
      </view>
      <view v-if="contents.length" class="batch-actions">
        <button class="mini ghost" @click="pickFriends">{{ $t('ie.batchPick') }}</button>
        <button
          class="mini"
          :disabled="!selectedIds.length || !batchFriends.length || batching"
          @click="submitBatch"
        >
          {{ $t('ie.batchSubmit') }}
        </button>
      </view>
      <view v-if="batchFriends.length" class="chips">
        <text v-for="f in batchFriends" :key="f.id" class="chip">{{ f.nickname }}</text>
      </view>
    </view>

    <FriendPicker v-model:visible="pickerVisible" :selected="batchFriends" @confirm="onFriendsConfirmed" />
  </view>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import FriendPicker from '../../components/FriendPicker.vue'
import { listMyContents, batchRecipients, type ContentItem, type FriendInfo } from '../../api/endpoints'
import { uploadFile, downloadBlob } from '../../api/client'
import { formatDateTime } from '../../utils/format'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'

onShow(() => {
  setNavTitle('hub.importExport')
})

const { t } = useI18n()

const exporting = ref(false)
const importing = ref(false)
const batching = ref(false)
const contents = ref<ContentItem[]>([])
const selectedIds = ref<string[]>([])
const batchFriends = ref<FriendInfo[]>([])
const pickerVisible = ref(false)

onMounted(loadContents)

async function loadContents() {
  // 批量编辑一次拉 100 条足够 MVP 场景；更多用「先筛选再说」
  const page = await listMyContents('', 0, 100)
  contents.value = page.items
}

function toggleContent(id: string) {
  if (selectedIds.value.includes(id)) {
    selectedIds.value = selectedIds.value.filter((x) => x !== id)
  } else {
    selectedIds.value = [...selectedIds.value, id]
  }
}

function pickFriends() {
  pickerVisible.value = true
}

function onFriendsConfirmed(friends: FriendInfo[]) {
  batchFriends.value = friends
}

async function submitBatch() {
  batching.value = true
  try {
    const res = await batchRecipients(
      selectedIds.value,
      batchFriends.value.map((f) => f.id)
    )
    uni.showToast({ title: t('ie.batchDone', { n: res.updated }), icon: 'success' })
    selectedIds.value = []
    batchFriends.value = []
  } catch {
    // 错误提示由请求层统一 toast
  } finally {
    batching.value = false
  }
}

async function doExport() {
  exporting.value = true
  try {
    const name = `hzone-export-${new Date().toISOString().slice(0, 10)}.zip`
    await downloadBlob('/api/export', name)
    uni.showToast({ title: t('ie.exported'), icon: 'success' })
  } catch {
    // 下载失败提示由 client 层统一 toast
  } finally {
    exporting.value = false
  }
}

function doImport() {
  // #ifdef H5
  const input = document.createElement('input')
  input.type = 'file'
  input.accept = '.zip'
  input.onchange = async () => {
    const file = input.files?.[0]
    if (!file) return
    importing.value = true
    try {
      // File 直传（uploadFile H5 fetch 分支），不经 blob: URL 中转
      await uploadFile('/api/import', file)
      uni.showToast({ title: t('ie.importSuccess'), icon: 'success' })
    } catch {
      // 错误提示由请求层统一 toast
    } finally {
      importing.value = false
    }
  }
  input.click()
  // #endif
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-sizing: border-box;
}
.section {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.section-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.btn {
  height: 42px;
  line-height: 42px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
.btn.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-border);
}
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 0;
  border-bottom: 1px solid var(--hz-color-border);
}
.row:last-child {
  border-bottom: none;
}
.row-main {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}
.row-title {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.check {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 1px solid var(--hz-color-border);
  flex-shrink: 0;
}
.check.on {
  border-color: var(--hz-color-primary);
  background: var(--hz-color-primary);
}
.batch-actions {
  display: flex;
  gap: 8px;
}
.mini {
  height: 34px;
  line-height: 34px;
  padding: 0 14px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-border);
}
.chips {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.chip {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  background: var(--hz-color-bg);
  border-radius: var(--hz-radius-md);
  padding: 2px 8px;
}
</style>
