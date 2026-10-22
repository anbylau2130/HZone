<template>
  <view class="page">
    <view class="section">
      <text class="section-title">{{ $t('friends.addSection') }}</text>
      <view class="add-row">
        <input v-model="targetUserId" class="field" :placeholder="$t('friends.targetUserId')" />
        <button class="pick" :disabled="!targetUserId.trim()" @click="sendRequest">
          {{ $t('friends.sendRequest') }}
        </button>
      </view>
      <text class="hint">{{ $t('friends.targetUserIdHint') }}</text>
    </view>

    <view v-if="requests.incoming.length" class="section">
      <text class="section-title">{{ $t('friends.incoming') }}</text>
      <view v-for="r in requests.incoming" :key="r.id" class="row">
        <text class="name">{{ r.requester?.nickname }}</text>
        <view class="actions">
          <button class="mini" @click="accept(r.id)">{{ $t('friends.accept') }}</button>
          <button class="mini ghost" @click="decline(r.id)">{{ $t('friends.decline') }}</button>
        </view>
      </view>
    </view>

    <view v-if="requests.outgoing.length" class="section">
      <text class="section-title">{{ $t('friends.outgoing') }}</text>
      <view v-for="r in requests.outgoing" :key="r.id" class="row">
        <text class="name">{{ r.addressee?.nickname }}</text>
        <text class="hint">{{ $t('friends.pendingLabel') }}</text>
      </view>
    </view>

    <view class="section">
      <text class="section-title">{{ $t('friends.listSection') }}</text>
      <view v-if="!friends.length" class="hint">
        <text class="hint-text">{{ $t('common.empty') }}</text>
      </view>
      <view v-for="f in friends" :key="f.id" class="row">
        <text class="name" @click="goProfile(f)">{{ f.nickname }}</text>
        <view class="actions">
          <button class="mini" @click="goProfile(f)">{{ $t('friends.viewProfile') }}</button>
          <button class="mini ghost danger" @click="remove(f)">{{ $t('friends.remove') }}</button>
        </view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { onShow } from '@dcloudio/uni-app'
import { setNavTitle } from '../../locale'
import {
  listFriends,
  listFriendRequests,
  sendFriendRequest,
  acceptFriendRequest,
  declineFriendRequest,
  removeFriend,
  type FriendInfo,
  type FriendRequests
} from '../../api/endpoints'

const { t } = useI18n()

const friends = ref<FriendInfo[]>([])
const requests = ref<FriendRequests>({ incoming: [], outgoing: [] })
const targetUserId = ref('')

onMounted(load)

onShow(() => {
  setNavTitle('hub.friends')
})

async function load() {
  const [f, r] = await Promise.all([listFriends(), listFriendRequests()])
  friends.value = f
  requests.value = r
}

async function sendRequest() {
  try {
    await sendFriendRequest(targetUserId.value.trim())
    uni.showToast({ title: t('friends.requestSent'), icon: 'success' })
    targetUserId.value = ''
    load()
  } catch {
    // 错误提示由请求层统一 toast
  }
}

async function accept(id: string) {
  await acceptFriendRequest(id)
  load()
}

async function decline(id: string) {
  await declineFriendRequest(id)
  load()
}

function remove(f: FriendInfo) {
  uni.showModal({
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
title: t('friends.remove'),
    content: t('friends.removeConfirm', { name: f.nickname }),
    success: async (res) => {
      if (res.confirm) {
        await removeFriend(f.id)
        load()
      }
    }
  })
}

function goProfile(f: FriendInfo) {
  uni.navigateTo({ url: `/pages/profile/profile?userId=${f.id}&nickname=${encodeURIComponent(f.nickname)}` })
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
.name {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.actions {
  display: flex;
  gap: 8px;
}
.mini {
  height: 30px;
  line-height: 30px;
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.mini.danger {
  color: #e54d42;
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
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
</style>
