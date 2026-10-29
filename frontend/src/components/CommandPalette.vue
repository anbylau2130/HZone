<template>
  <!-- 命令面板（桌面键盘向）：Ctrl/⌘+K 唤起，↑↓ 选择、回车执行、Esc 关闭。
       命令集 = 页面导航 + 主题切换 + 「在全局搜索中查 xxx」fallback。 -->
  <view v-if="open" class="cp-overlay" @click="open = false">
    <view class="cp-panel" @click.stop>
      <input
        ref="inputRef"
        v-model="keyword"
        class="cp-input"
        :placeholder="$t('cmd.placeholder')"
        @keydown="onKeydown"
      />
      <scroll-view scroll-y class="cp-list">
        <view
          v-for="(c, i) in filtered"
          :key="c.key"
          class="cp-item"
          :class="{ on: i === active }"
          @click="run(c)"
          @mouseenter="active = i"
        >
          <text class="cp-item-ico">{{ c.icon }}</text>
          <text class="cp-item-label">{{ c.label }}</text>
          <text v-if="c.hint" class="cp-item-hint">{{ c.hint }}</text>
        </view>
        <view v-if="!filtered.length" class="cp-empty">
          <text class="cp-empty-text">{{ $t('cmd.none') }}</text>
        </view>
      </scroll-view>
      <view class="cp-foot">
        <text class="cp-foot-text">↑↓ {{ $t('cmd.navigate') }} · ↵ {{ $t('cmd.run') }} · Esc {{ $t('cmd.close') }}</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useThemeStore } from '../stores/theme'
import { presets } from '../theme/presets'

const { t } = useI18n()
const theme = useThemeStore()

const open = ref(false)
const keyword = ref('')
const active = ref(0)
const inputRef = ref<unknown>(null)

interface Command {
  key: string
  icon: string
  label: string
  hint?: string
  action: () => void
}

/** 命令集：页面导航 + 动作 + 主题（关键词实时过滤；搜索行动态追加）。 */
const commands = computed<Command[]>(() => {
  const nav = (icon: string, labelKey: string, url: string): Command => ({
    key: `nav-${url}`,
    icon,
    label: t(labelKey),
    action: () => uni.redirectTo({ url })
  })
  const list: Command[] = [
    nav('⌂', 'tabs.index', '/pages/home/home'),
    nav('✎', 'tabs.moments', '/pages/moments/moments'),
    nav('▤', 'tabs.journals', '/pages/journals/journals'),
    nav('◉', 'tabs.received', '/pages/received/received'),
    nav('⚙', 'tabs.settings', '/pages/settings/settings'),
    {
      key: 'publish-say',
      icon: '✏️',
      label: t('cmd.publishSay'),
      action: () => uni.navigateTo({ url: '/pages/publish/publish?kind=say' })
    },
    {
      key: 'publish-journal',
      icon: '📝',
      label: t('cmd.publishJournal'),
      action: () => uni.navigateTo({ url: '/pages/publish/publish?kind=journal' })
    },
    nav('🔍', 'hub.search', '/pages/search/search'),
    nav('💬', 'ai.title', '/pages/ai/chat'),
    nav('🧾', 'ledger.title', '/pages/ledger/ledger'),
    nav('📊', 'report.title', '/pages/report/report'),
    nav('🖼️', 'photos.title', '/pages/photos/photos'),
    nav('🔔', 'hub.inbox', '/pages/inbox/inbox')
  ]
  // 主题切换：当前主题之外的前几项
  for (const p of presets) {
    list.push({
      key: `theme-${p.id}`,
      icon: '🎨',
      label: t('cmd.theme', { name: p.name }),
      hint: theme.presetId === p.id ? t('cmd.current') : undefined,
      action: () => theme.setPreset(p.id)
    })
  }
  return list
})

const filtered = computed<Command[]>(() => {
  const kw = keyword.value.trim().toLowerCase()
  const matched = kw
    ? commands.value.filter((c) => c.label.toLowerCase().includes(kw))
    : commands.value
  const out = matched.slice(0, 12)
  // 搜索 fallback：有输入且没精确导航意图时，追加一行"全局搜索 xxx"
  // 搜索 fallback：有输入时追加一行"全局搜索 xxx"（词走 URL query，页面 onShow 消费）。
  // 注意：run() 会先清空 keyword 再执行 action——词必须在此刻捕获进闭包。
  if (kw && keyword.value.trim().length > 0) {
    const q0 = keyword.value.trim()
    out.push({
      key: 'search-fallback',
      icon: '🔎',
      label: t('cmd.searchFor', { q: q0 }),
      action: () => {
        uni.navigateTo({ url: `/pages/search/search?q=${encodeURIComponent(q0)}` })
      }
    })
  }
  return out
})

function run(c: Command) {
  open.value = false
  keyword.value = ''
  c.action()
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    active.value = (active.value + 1) % Math.max(1, filtered.value.length)
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    active.value = (active.value - 1 + filtered.value.length) % Math.max(1, filtered.value.length)
  } else if (e.key === 'Enter') {
    const c = filtered.value[active.value]
    if (c) run(c)
  }
}

function onGlobalKeydown(e: KeyboardEvent) {
  if ((e.ctrlKey || e.metaKey) && (e.key === 'k' || e.key === 'K')) {
    e.preventDefault()
    open.value = !open.value
    if (open.value) {
      active.value = 0
      keyword.value = ''
      nextTick(() => {
        const el = (inputRef.value as { $el?: HTMLElement } | null)?.$el
        const input = el?.querySelector?.('input') ?? (el as HTMLInputElement | null)
        input?.focus?.()
      })
    }
  } else if (e.key === 'Escape' && open.value) {
    open.value = false
  }
}

onMounted(() => {
  window.addEventListener('keydown', onGlobalKeydown)
})
onUnmounted(() => window.removeEventListener('keydown', onGlobalKeydown))
</script>

<style scoped>
.cp-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding-top: 12vh;
  z-index: 2000;
}
.cp-panel {
  width: 92%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.22);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.cp-input {
  height: 46px;
  padding: 0 16px;
  border-bottom: 1px solid var(--hz-color-border);
  font-size: 16px;
  color: var(--hz-color-text);
  background: transparent;
}
.cp-list {
  max-height: 320px;
}
.cp-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 9px 14px;
  cursor: pointer;
}
.cp-item.on {
  background: var(--hz-color-bg);
}
.cp-item-ico {
  font-size: 15px;
  width: 20px;
  text-align: center;
}
.cp-item-label {
  flex: 1;
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.cp-item.on .cp-item-label {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.cp-item-hint {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
.cp-empty {
  padding: 22px 0;
  display: flex;
  justify-content: center;
}
.cp-empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.cp-foot {
  padding: 7px 14px;
  border-top: 1px solid var(--hz-color-border);
}
.cp-foot-text {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
</style>
