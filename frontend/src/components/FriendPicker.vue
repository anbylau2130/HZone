<template>
  <view v-if="visible" class="overlay" @click="close">
    <view class="sheet" @click.stop>
      <view class="head">
        <text class="title">{{ $t('friends.pickerTitle') }}</text>
        <text class="cancel" @click="close">{{ $t('common.cancel') }}</text>
      </view>
      <scroll-view scroll-y class="list">
        <view v-if="loading" class="hint">
          <text class="hint-text">{{ $t('common.loading') }}</text>
        </view>
        <view v-else-if="!friends.length" class="hint">
          <text class="hint-text">{{ $t('common.empty') }}</text>
        </view>
        <view
          v-for="f in friends"
          :key="f.id"
          class="row"
          @click="toggle(f)"
        >
          <text class="name">{{ f.nickname }}</text>
          <view class="check" :class="{ on: selectedIds.includes(f.id) }" />
        </view>
      </scroll-view>
      <button class="btn" :disabled="!selectedIds.length" @click="confirm">
        {{ $t('common.confirm') }}
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { listFriends, type FriendInfo } from '../api/endpoints'

const props = defineProps<{ visible: boolean }>()

const emit = defineEmits<{
  (e: 'update:visible', v: boolean): void
  (e: 'confirm', friends: FriendInfo[]): void
}>()

const friends = ref<FriendInfo[]>([])
const selectedIds = ref<string[]>([])
const loading = ref(false)

watch(
  () => props.visible,
  (v) => {
    if (v) {
      selectedIds.value = []
      load()
    }
  }
)

async function load() {
  loading.value = true
  try {
    friends.value = await listFriends()
  } catch {
    friends.value = []
  } finally {
    loading.value = false
  }
}

function toggle(f: FriendInfo) {
  const idx = selectedIds.value.indexOf(f.id)
  if (idx >= 0) {
    selectedIds.value = selectedIds.value.filter((id) => id !== f.id)
  } else {
    selectedIds.value = [...selectedIds.value, f.id]
  }
}

function confirm() {
  const picked = friends.value.filter((f) => selectedIds.value.includes(f.id))
  emit('confirm', picked)
  close()
}

function close() {
  emit('update:visible', false)
}
</script>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.sheet {
  width: 100%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.list {
  max-height: 50vh;
}
.row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 4px;
  border-bottom: 1px solid var(--hz-color-border);
}
.name {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.check {
  width: 20px;
  height: 20px;
  border-radius: 50%;
  border: 1px solid var(--hz-color-border);
}
.check.on {
  border-color: var(--hz-color-primary);
  background: var(--hz-color-primary);
}
.btn {
  height: 42px;
  line-height: 42px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-base);
}
</style>
