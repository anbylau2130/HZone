<template>
  <view class="page">
    <!-- 账本切换行：个人账本（自动）+ 共同账本 + ＋新建 + ⋯管理 -->
    <view class="lg-tabs">
      <scroll-view scroll-x class="lg-tabs-scroll" :show-scrollbar="false">
        <view class="lg-tabs-row">
          <view
            v-for="l in ledgers"
            :key="l.id"
            class="lg-chip"
            :class="{ on: l.id === activeId }"
            @click="switchLedger(l.id)"
          >
            <text class="lg-chip-name">{{ l.name }}</text>
            <text v-if="l.isShared" class="lg-chip-shared">共</text>
          </view>
        </view>
      </scroll-view>
      <view class="lg-tab-op" :title="$t('ledger.newShared')" @click="createShared">＋</view>
      <view class="lg-tab-op" :title="$t('common.more')" @click="manageLedger">⋯</view>
    </view>

    <!-- 月份切换 + 汇总卡 -->
    <view class="lg-month">
      <text class="lg-month-arrow" @click="shiftMonth(-1)">‹</text>
      <text class="lg-month-text">{{ monthLabel }}</text>
      <text class="lg-month-arrow" @click="shiftMonth(1)">›</text>
    </view>
    <view class="lg-summary">
      <view class="lg-sum-col">
        <text class="lg-sum-label">{{ $t('ledger.expense') }}</text>
        <text class="lg-sum-num expense">{{ fmt(month?.summary.expenseCents ?? 0) }}</text>
      </view>
      <view class="lg-sum-col">
        <text class="lg-sum-label">{{ $t('ledger.income') }}</text>
        <text class="lg-sum-num income">{{ fmt(month?.summary.incomeCents ?? 0) }}</text>
      </view>
      <view class="lg-sum-col">
        <text class="lg-sum-label">{{ $t('ledger.balance') }}</text>
        <text class="lg-sum-num">{{ fmt((month?.summary.incomeCents ?? 0) - (month?.summary.expenseCents ?? 0)) }}</text>
      </view>
    </view>

    <!-- 分类占比条（支出分类，本月） -->
    <view v-if="catBars.length" class="lg-cats">
      <view
        v-for="c in catBars"
        :key="c.category"
        class="lg-cat-row"
        @click="pickEntry(c.category)"
      >
        <text class="lg-cat-name">{{ catLabel(c.category) }}</text>
        <view class="lg-cat-bar">
          <view class="lg-cat-fill" :style="{ width: c.percent + '%' }" />
        </view>
        <text class="lg-cat-amount">{{ fmt(c.cents) }}</text>
      </view>
    </view>

    <!-- 条目列表：按日分组 -->
    <view v-if="!month || (!month.entries.length && !loading)" class="lg-empty">
      <text class="lg-empty-text">{{ $t('common.empty') }}</text>
    </view>
    <view v-for="g in dayGroups" :key="g.date" class="lg-day">
      <view class="lg-day-head">
        <text class="lg-day-date">{{ g.date }}</text>
        <text class="lg-day-sum">{{ $t('ledger.expense') }} {{ fmt(g.expenseCents) }}</text>
      </view>
      <view v-for="e in g.items" :key="e.id" class="lg-row" @click="editEntry(e)">
        <text class="lg-row-ico">{{ catIcon(e.category, e.kind) }}</text>
        <view class="lg-row-mid">
          <text class="lg-row-note">{{ e.note || catLabel(e.category) }}</text>
          <text v-if="activeLedger?.isShared" class="lg-row-by">{{ e.recorder.nickname }} · {{ catLabel(e.category) }}</text>
        </view>
        <text class="lg-row-amount" :class="e.kind">{{ sign(e.kind) }}{{ fmt(e.amountCents) }}</text>
        <text class="lg-row-del" @click.stop="removeEntry(e)">✕</text>
      </view>
    </view>

    <view v-if="loading" class="lg-empty">
      <text class="lg-empty-text">{{ $t('common.loading') }}</text>
    </view>

    <!-- 记一笔 -->
    <view class="fab" @click="openEntry(null)">{{ $t('ledger.add') }}</view>

    <!-- 记一笔/编辑弹层 -->
    <view v-if="entryOpen" class="lg-overlay" @click="entryOpen = false">
      <view class="lg-sheet" @click.stop>
        <view class="lg-sheet-head">
          <text class="lg-sheet-title">{{ editingId ? $t('common.save') : $t('ledger.add') }}</text>
          <text class="lg-cancel" @click="entryOpen = false">{{ $t('common.cancel') }}</text>
        </view>
        <view class="lg-kind">
          <view class="lg-kind-btn" :class="{ on: draft.kind === 'expense' }" @click="draft.kind = 'expense'">
            {{ $t('ledger.expense') }}
          </view>
          <view class="lg-kind-btn" :class="{ on: draft.kind === 'income' }" @click="draft.kind = 'income'; draft.category = defaultCategory('income')">
            {{ $t('ledger.income') }}
          </view>
        </view>
        <input
          v-model="draft.amount"
          class="lg-amount"
          type="digit"
          focus
          :placeholder="$t('ledger.amount')"
        />
        <view class="lg-cat-grid">
          <view
            v-for="c in categoriesOf(draft.kind)"
            :key="c.key"
            class="lg-cat-cell"
            :class="{ on: draft.category === c.key }"
            @click="draft.category = c.key"
          >
            <text class="lg-cat-cell-ico">{{ c.icon }}</text>
            <text class="lg-cat-cell-name">{{ c.label }}</text>
          </view>
        </view>
        <view class="lg-entry-row">
          <input v-model="draft.note" class="lg-note" :placeholder="$t('ledger.notePlaceholder')" :maxlength="200" />
          <picker mode="date" :value="draft.date" @change="draft.date = $event.detail.value">
            <view class="lg-date">{{ draft.date }}</view>
          </picker>
        </view>
        <button class="lg-save" :disabled="!canSave || saving" @click="saveEntry">
          {{ saving ? $t('common.loading') : $t('common.confirm') }}
        </button>
      </view>
    </view>

    <!-- 邀请好友（复用 FriendPicker，单选语义：确认后邀请列表里的人） -->
    <FriendPicker v-model:visible="inviteOpen" @confirm="onInviteConfirm" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import FriendPicker from '../../components/FriendPicker.vue'
import {
  listLedgers,
  createLedger,
  renameLedger,
  deleteLedger,
  inviteLedgerMember,
  removeLedgerMember,
  listLedgerEntries,
  createLedgerEntry,
  updateLedgerEntry,
  deleteLedgerEntry,
  type LedgerInfo,
  type LedgerEntryItem,
  type LedgerMonth
} from '../../api/endpoints'
import type { FriendInfo } from '../../api/endpoints'

const { t } = useI18n()

// ---------- 内置分类（key → 图标 + i18n 文案；自定义分类暂缓） ----------
const CATEGORIES = {
  expense: [
    { key: 'food', icon: '🍜' },
    { key: 'transport', icon: '🚌' },
    { key: 'shopping', icon: '🛍' },
    { key: 'daily', icon: '🧻' },
    { key: 'fun', icon: '🎮' },
    { key: 'medical', icon: '💊' },
    { key: 'home', icon: '🏠' },
    { key: 'otherExpense', icon: '📦' }
  ],
  income: [
    { key: 'salary', icon: '💰' },
    { key: 'invest', icon: '📈' },
    { key: 'bonus', icon: '🧧' },
    { key: 'otherIncome', icon: '📥' }
  ]
} as const

function categoriesOf(kind: 'expense' | 'income') {
  return (CATEGORIES[kind] as readonly { key: string; icon: string }[]).map((c) => ({
    ...c,
    label: t(`ledger.cat.${c.key}`)
  }))
}
function defaultCategory(kind: 'expense' | 'income') {
  return CATEGORIES[kind][0].key
}
function catLabel(key: string) {
  return t(`ledger.cat.${key}`)
}
function catIcon(key: string, kind: 'expense' | 'income') {
  const pool = CATEGORIES[kind] as readonly { key: string; icon: string }[]
  return (pool.find((c) => c.key === key) ?? CATEGORIES.expense[7]).icon
}

/** 分 → ¥ 文案（两位小数，去尾零：1280 → ¥12.8、1300 → ¥13）。 */
function fmt(cents: number): string {
  const s = (cents / 100).toFixed(2)
  return `¥${s.replace(/\.00$/, '').replace(/(\.\d)0$/, '$1')}`
}
function sign(kind: string) {
  return kind === 'income' ? '+' : '-'
}

// ---------- 账本 + 月度数据 ----------
const ledgers = ref<LedgerInfo[]>([])
const activeId = ref<string | null>(null)
const month = ref<LedgerMonth | null>(null)
const loading = ref(false)
// 月份游标（本地时区年月）
const monthCursor = ref(new Date())

const activeLedger = computed(() => ledgers.value.find((l) => l.id === activeId.value) ?? null)
const monthLabel = computed(() => {
  const d = monthCursor.value
  return `${d.getFullYear()}年${String(d.getMonth() + 1).padStart(2, '0')}月`
})
function monthKey(): string {
  const d = monthCursor.value
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

async function loadLedgers(keepActive = true) {
  const res = await listLedgers()
  ledgers.value = res.items
  const mine = res.items.find((l) => l.id === activeId.value)
  if (!keepActive || !mine) activeId.value = res.items[0]?.id ?? null
}

async function loadMonth() {
  if (!activeId.value) return
  loading.value = true
  try {
    month.value = await listLedgerEntries(activeId.value, monthKey())
  } catch {
    // 请求层已 toast
  } finally {
    loading.value = false
  }
}

function switchLedger(id: string) {
  if (id === activeId.value) return
  activeId.value = id
  void loadMonth()
}

function shiftMonth(delta: number) {
  const d = new Date(monthCursor.value)
  d.setMonth(d.getMonth() + delta)
  monthCursor.value = d
  void loadMonth()
}

onShow(() => {
  void (async () => {
    await loadLedgers()
    await loadMonth()
  })()
})

// ---------- 分类占比条 ----------
const catBars = computed(() => {
  const cats = month.value?.summary.byCategory ?? []
  const total = cats.reduce((s, c) => s + c.cents, 0)
  if (!total) return []
  return cats.map((c) => ({ ...c, percent: Math.round((c.cents / total) * 100) }))
})

// ---------- 按日分组 ----------
const dayGroups = computed(() => {
  const map = new Map<string, { date: string; items: LedgerEntryItem[]; expenseCents: number }>()
  for (const e of month.value?.entries ?? []) {
    const key = e.entryDate.slice(0, 10)
    if (!map.has(key)) map.set(key, { date: key, items: [], expenseCents: 0 })
    const g = map.get(key)!
    g.items.push(e)
    if (e.kind === 'expense') g.expenseCents += e.amountCents
  }
  return [...map.values()]
})

// ---------- 记一笔 / 编辑 ----------
const entryOpen = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)
const draft = ref<{ amount: string; kind: 'expense' | 'income'; category: string; note: string; date: string }>({
  amount: '',
  kind: 'expense',
  category: 'food',
  note: '',
  date: today()
})

function today(): string {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const canSave = computed(() => Number.parseFloat(draft.value.amount) > 0)

function openEntry(category?: string) {
  editingId.value = null
  draft.value = { amount: '', kind: 'expense', category: category ?? 'food', note: '', date: today() }
  entryOpen.value = true
}

function editEntry(e: LedgerEntryItem) {
  draft.value = {
    amount: (e.amountCents / 100).toString(),
    kind: e.kind,
    category: e.category,
    note: e.note ?? '',
    date: e.entryDate.slice(0, 10)
  }
  editingId.value = e.id
  entryOpen.value = true
}

async function saveEntry() {
  if (!canSave.value || saving.value || !activeId.value) return
  const cents = Math.round(Number.parseFloat(draft.value.amount) * 100)
  if (!Number.isFinite(cents) || cents <= 0) return
  saving.value = true
  try {
    const payload = {
      amountCents: cents,
      kind: draft.value.kind,
      category: draft.value.category,
      note: draft.value.note.trim() || undefined,
      entryDate: draft.value.date
    }
    if (editingId.value) await updateLedgerEntry(editingId.value, payload)
    else await createLedgerEntry(activeId.value, payload)
    entryOpen.value = false
    await loadMonth()
  } catch {
    // 请求层已 toast
  } finally {
    saving.value = false
  }
}

function removeEntry(e: LedgerEntryItem) {
  uni.showModal({
    title: t('ledger.deleteEntryConfirm'),
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (m) => {
      if (!m.confirm) return
      try {
        await deleteLedgerEntry(e.id)
        await loadMonth()
      } catch {
        // 请求层已 toast
      }
    }
  })
}

// ---------- 共同账本管理 ----------
function promptName(title: string, initial: string, onDone: (name: string) => void) {
  uni.showModal({
    title,
    editable: true,
    placeholderText: t('ledger.sharedNamePlaceholder'),
    content: initial,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: (res) => {
      const name = (res.content ?? '').trim()
      if (!res.confirm || !name) return
      onDone(name)
    }
  })
}

function createShared() {
  promptName(t('ledger.newShared'), '', async (name) => {
    try {
      const created = await createLedger(name)
      await loadLedgers()
      switchLedger(created.id)
    } catch {
      // 请求层已 toast
    }
  })
}

const inviteOpen = ref(false)

function manageLedger() {
  const ledger = activeLedger.value
  if (!ledger) return
  if (!ledger.isShared) {
    // 个人账本：无管理项（不可删不可邀）
    uni.showToast({ title: t('ledger.personalProtected'), icon: 'none' })
    return
  }
  const actions = [t('ledger.renameLedger'), t('ledger.invite')]
  uni.showActionSheet({
    itemList: actions,
    success: (res) => {
      if (res.tapIndex === 0) {
        promptName(t('ledger.renameLedger'), ledger.name, async (name) => {
          try {
            await renameLedger(ledger.id, name)
            await loadLedgers()
          } catch {
            // 请求层已 toast
          }
        })
      } else if (res.tapIndex === 1) {
        inviteOpen.value = true
      }
    }
  })
}

async function onInviteConfirm(friends: FriendInfo[]) {
  if (!activeLedger.value || !friends.length) return
  try {
    for (const f of friends) {
      await inviteLedgerMember(activeLedger.value.id, f.id)
    }
    await loadLedgers()
    uni.showToast({ title: t('publish.success'), icon: 'success' })
  } catch {
    // 请求层已 toast（非好友/已在成员）
  }
}
</script>

<style scoped>
.page {
  min-height: 100vh;
  background: var(--hz-color-bg);
  padding: 16px 16px 96px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
/* 账本 chips */
.lg-tabs {
  display: flex;
  align-items: center;
  gap: 6px;
}
.lg-tabs-scroll {
  flex: 1;
  min-width: 0;
  white-space: nowrap;
}
.lg-tabs-row {
  display: inline-flex;
  gap: 6px;
}
.lg-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  padding: 4px 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  cursor: pointer;
  flex-shrink: 0;
  background: var(--hz-color-surface);
}
.lg-chip.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.lg-chip-name {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  max-width: 96px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.lg-chip.on .lg-chip-name {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.lg-chip-shared {
  font-size: 10px;
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
  border-radius: 4px;
  padding: 0 3px;
  line-height: 1.4;
}
.lg-tab-op {
  flex-shrink: 0;
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px dashed var(--hz-color-border);
  border-radius: 50%;
  font-size: 15px;
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
/* 月份 + 汇总 */
.lg-month {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
}
.lg-month-arrow {
  font-size: 22px;
  color: var(--hz-color-text-secondary);
  padding: 0 8px;
  cursor: pointer;
}
.lg-month-text {
  font-size: 16px;
  font-weight: 700;
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.lg-summary {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 14px;
  display: flex;
}
.lg-sum-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: center;
}
.lg-sum-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.lg-sum-num {
  font-size: 18px;
  font-weight: 700;
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.lg-sum-num.expense {
  color: #e54d42;
}
.lg-sum-num.income {
  color: var(--hz-color-primary);
}
/* 分类占比条 */
.lg-cats {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
.lg-cat-row {
  display: flex;
  align-items: center;
  gap: 8px;
}
.lg-cat-name {
  width: 48px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  flex-shrink: 0;
}
.lg-cat-bar {
  flex: 1;
  height: 8px;
  border-radius: 4px;
  background: var(--hz-color-bg);
  overflow: hidden;
}
.lg-cat-fill {
  height: 100%;
  background: var(--hz-color-primary);
  border-radius: 4px;
  min-width: 4px;
}
.lg-cat-amount {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}
/* 按日分组条目 */
.lg-day {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.lg-day-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  padding: 0 4px;
}
.lg-day-date {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text);
}
.lg-day-sum {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  font-variant-numeric: tabular-nums;
}
.lg-row {
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-md);
  padding: 10px 12px;
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
}
.lg-row-ico {
  font-size: 18px;
  line-height: 1;
}
.lg-row-mid {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.lg-row-note {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.lg-row-by {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
.lg-row-amount {
  font-size: 15px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
  flex-shrink: 0;
}
.lg-row-amount.expense {
  color: var(--hz-color-text);
}
.lg-row-amount.income {
  color: var(--hz-color-primary);
}
.lg-row-del {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
  padding: 4px;
  cursor: pointer;
}
.lg-empty {
  padding: 40px 0;
  display: flex;
  justify-content: center;
}
.lg-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 记一笔 FAB */
.fab {
  position: fixed;
  right: 20px;
  bottom: calc(24px + env(safe-area-inset-bottom));
  height: 40px;
  line-height: 40px;
  padding: 0 18px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
  font-size: var(--hz-font-size-base);
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.18);
  cursor: pointer;
  z-index: 60;
}
/* 记一笔弹层 */
.lg-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.lg-sheet {
  width: 100%;
  max-width: 560px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.lg-sheet-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.lg-sheet-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.lg-cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.lg-kind {
  display: flex;
  gap: 8px;
}
.lg-kind-btn {
  flex: 1;
  height: 34px;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
.lg-kind-btn.on {
  border-color: var(--hz-color-primary);
  color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 8%, transparent);
  font-weight: 600;
}
.lg-amount {
  height: 52px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 16px;
  font-size: 26px;
  font-weight: 700;
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
  font-variant-numeric: tabular-nums;
}
.lg-cat-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
}
.lg-cat-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 3px;
  padding: 8px 0;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.lg-cat-cell.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 8%, transparent);
}
.lg-cat-cell-ico {
  font-size: 20px;
  line-height: 1;
}
.lg-cat-cell-name {
  font-size: 11px;
  color: var(--hz-color-text);
}
.lg-entry-row {
  display: flex;
  gap: 8px;
  align-items: center;
}
.lg-note {
  flex: 1;
  height: 38px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.lg-date {
  height: 38px;
  line-height: 38px;
  padding: 0 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  font-variant-numeric: tabular-nums;
}
.lg-save {
  height: 40px;
  line-height: 40px;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: 999px;
  font-size: var(--hz-font-size-base);
}
.lg-save[disabled] {
  opacity: 0.55;
}
</style>
