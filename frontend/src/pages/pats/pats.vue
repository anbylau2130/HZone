<template>
  <view class="page">
    <view class="section">
      <text class="section-title">{{ $t('pats.createSection') }}</text>
      <view class="add-row">
        <input v-model="patName" class="field" :placeholder="$t('pats.namePlaceholder')" />
        <button class="pick" :disabled="!patName.trim()" @click="create">{{ $t('pats.create') }}</button>
      </view>
      <view v-if="newToken" class="token-box">
        <text class="token-text">{{ newToken }}</text>
        <button class="mini" @click="copyToken">{{ $t('common.copy') }}</button>
      </view>
      <text v-if="newToken" class="hint">{{ $t('pats.tokenOnceNote') }}</text>
    </view>

    <view class="section">
      <text class="section-title">{{ $t('pats.listSection') }}</text>
      <view v-if="!items.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>
      <view v-for="p in items" :key="p.id" class="row">
        <view class="row-main">
          <text class="name">{{ p.name }}</text>
          <text class="hint">{{ statusLabel(p) }}</text>
        </view>
        <button v-if="!p.revokedAt" class="mini ghost danger" @click="revoke(p.id)">
          {{ $t('pats.revoke') }}
        </button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { listPats, createPat, revokePat, type PatItem } from '../../api/endpoints'
import { formatDateTime } from '../../utils/format'

const { t } = useI18n()

const items = ref<PatItem[]>([])
const patName = ref('')
const newToken = ref('')

onMounted(load)

async function load() {
  items.value = (await listPats()).items
}

async function create() {
  const name = patName.value.trim()
  if (!name) return
  try {
    const created = await createPat(name)
    // 明文 token 仅此一次返回，先展示再刷新列表。
    newToken.value = created.token
    patName.value = ''
    load()
  } catch {
    // 错误提示由请求层统一 toast
  }
}

function copyToken() {
  uni.setClipboardData({
    data: newToken.value,
    success: () => {
      uni.showToast({ title: t('common.copied'), icon: 'success' })
    }
  })
}

async function revoke(id: string) {
  try {
    await revokePat(id)
    load()
  } catch {
    // 错误提示由请求层统一 toast
  }
}

function statusLabel(p: PatItem) {
  if (p.revokedAt) {
    return t('pats.revokedAt', { time: formatDateTime(p.revokedAt) })
  }
  return `${t('pats.active')} · ${formatDateTime(p.createdAt)}`
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
.add-row {
  display: flex;
  gap: 8px;
}
.field {
  flex: 1;
  height: 38px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.pick {
  height: 38px;
  line-height: 38px;
  padding: 0 14px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
}
.token-box {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--hz-color-bg);
  border: 1px dashed var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  padding: 10px 12px;
}
.token-text {
  flex: 1;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  word-break: break-all;
}
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 0;
  border-bottom: 1px solid var(--hz-color-border);
}
.row:last-child {
  border-bottom: none;
}
.row-main {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.name {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.mini {
  height: 32px;
  line-height: 32px;
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost.danger {
  background: var(--hz-color-surface);
  color: #e54d42;
  border: 1px solid var(--hz-color-border);
}
</style>
