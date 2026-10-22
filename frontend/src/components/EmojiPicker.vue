<template>
  <view class="emoji-panel">
    <view class="emoji-tabs">
      <text
        v-for="c in tabs"
        :key="c.key"
        class="tab"
        :class="{ on: active === c.key }"
        @click="active = c.key"
      >
        {{ c.icon }}
      </text>
    </view>
    <scroll-view scroll-y class="grid-wrap">
      <view v-if="current.length" class="grid">
        <text v-for="(e, i) in current" :key="active + i" class="cell" @click="pick(e)">
          {{ e }}
        </text>
      </view>
      <view v-else class="empty">
        <text class="empty-text">{{ $t('publish.emojiNoRecent') }}</text>
      </view>
    </scroll-view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'

// 说说表情包：常用 Unicode emoji 精选集（每类约 40-60 个，够日常表达且面板轻量）。
// "最近"存 localStorage（≤24，去重置顶），点击即回填并保持面板开启便于连发。
const emit = defineEmits<{ (e: 'select', emoji: string): void }>()

const SMILEYS = '😀 😁 😂 🤣 😊 😇 🙂 🙃 😉 😍 🥰 😘 😗 😋 😛 🤪 🤨 🧐 🤓 😎 🥳 😏 😒 😔 😟 🙁 ☹️ 😣 😖 😫 😩 🥺 😢 😭 😤 😠 🤯 😳 🥵 🥶 😱 😨 😰 🤗 🤔 🤭 🤫 😴 🥱'.split(' ')
const GESTURES = '👍 👎 👌 ✌️ 🤞 🤟 🤘 🤙 👈 👉 👆 👇 ☝️ 👋 🖐 ✋ 🖖 👏 🙌 🤲 🙏 ✍️ 💪 🤳 👀 🧠 🫶 🤝 👊 ✊ 🤲 💅'.split(' ')
const ANIMALS = '🐱 🐶 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🙈 🙉 🙊 🐔 🐧 🐦 🐤 🦆 🦅 🦉 🐺 🐴 🦄 🐝 🐛 🦋 🐌 🐞 🐢 🐍 🦎 🐙 🦑 🦀 🐠 🐟 🐬 🐳 🦈'.split(' ')
const FOODS = '🍏 🍎 🍐 🍊 🍋 🍌 🍉 🍇 🍓 🫐 🍈 🍒 🍑 🥭 🍍 🥥 🥝 🍅 🥑 🍆 🥔 🥕 🌽 🌶 🥒 🥬 🥦 🍄 🥜 🍞 🥐 🥖 🧀 🥚 🍳 🥓 🍗 🍖 🌭 🍔 🍟 🍕 🥪 🌮 🍜 🍲 🍣 🍱 🍚 🍛 🍦 🍰 🎂 🍺 🍵 ☕ 🧋'.split(' ')
const ACTIVITIES = '⚽ 🏀 ⚾ 🎾 🏐 🎱 🏓 🏸 ⛳ 🏹 🎣 🥊 🥋 🛹 🏆 🥇 🥈 🥉 🏅 🎖 🎪 🤹 🎭 🎨 🎬 🎤 🎧 🎼 🎹 🥁 🎷 🎺 🎸 🎻 🎲 ♟ 🎯 🎳 🎮 🧩'.split(' ')
const OBJECTS = '⌚ 📱 💻 ⏰ ⏳ 📷 💡 🔍 🔑 🚪 🛋 🛏 🧸 📚 ✂️ 📎 📌 📝 ✏️ 📖 📅 💰 💳 💎 🔧 🔨 ⚙ 🧲 💊 🩹 🚀 ✈️ 🚗 🚌 🚲 🛵 🏍 🗺 🏔 🌋 🏝 🏕 ⛺ 🌅 🌈 🌃 🌉'.split(' ')
const SYMBOLS = '❤️ 🧡 💛 💚 💙 💜 🖤 🤍 💔 ❣️ 💕 💞 💓 💗 💖 💘 💝 🔥 ✨ 🌟 💫 ⭐ 🌈 ☀ ⛅ 🌧 ⛈ ❄️ 🌊 🎉 🎊 🎈 🎁 🏮 🧧 📮 ⚠️ ♻️ ✅ ❌ ❓ ❗ 💯 🚫'.split(' ')

const tabs = [
  { key: 'recent', icon: '🕘' },
  { key: 'smileys', icon: '😊' },
  { key: 'gestures', icon: '👍' },
  { key: 'animals', icon: '🐼' },
  { key: 'foods', icon: '🍉' },
  { key: 'activities', icon: '⚽' },
  { key: 'objects', icon: '💡' },
  { key: 'symbols', icon: '❤️' }
] as const

const active = ref<(typeof tabs)[number]['key']>('smileys')

const RECENT_KEY = 'hz-emoji-recent'
const recent = ref<string[]>(readRecent())

function readRecent(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY)
    const list = raw ? (JSON.parse(raw) as string[]) : []
    return Array.isArray(list) ? list.slice(0, 24) : []
  } catch {
    return []
  }
}

const current = computed(() => {
  switch (active.value) {
    case 'recent': return recent.value
    case 'gestures': return GESTURES
    case 'animals': return ANIMALS
    case 'foods': return FOODS
    case 'activities': return ACTIVITIES
    case 'objects': return OBJECTS
    case 'symbols': return SYMBOLS
    default: return SMILEYS
  }
})

function pick(emoji: string) {
  recent.value = [emoji, ...recent.value.filter((e) => e !== emoji)].slice(0, 24)
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify(recent.value))
  } catch {
    // 存储满等异常静默：最近使用是尽力而为
  }
  emit('select', emoji)
}
</script>

<style scoped>
/* 表情面板：紧凑排布（小格子高密度网格），与媒体区块同款软面板 */
.emoji-panel {
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 6px 8px 8px;
  animation: emoji-in 0.16s ease-out;
  transform-origin: bottom;
}
@keyframes emoji-in {
  from {
    opacity: 0;
    transform: translateY(6px) scaleY(0.96);
  }
  to {
    opacity: 1;
    transform: translateY(0) scaleY(1);
  }
}
.emoji-tabs {
  display: flex;
  gap: 2px;
  border-bottom: 1px solid var(--hz-color-border);
  padding-bottom: 4px;
  overflow-x: auto;
  scrollbar-width: none;
}
.emoji-tabs::-webkit-scrollbar {
  display: none;
}
.tab {
  flex-shrink: 0;
  width: 30px;
  height: 26px;
  line-height: 26px;
  text-align: center;
  font-size: 15px;
  border-radius: var(--hz-radius-md);
  opacity: 0.55;
  transition: opacity 0.12s ease, background 0.12s ease;
}
.tab.on {
  opacity: 1;
  background: var(--hz-color-surface);
}
.grid-wrap {
  max-height: 180px;
}
.grid {
  /* 固定列宽 + 左对齐：格子不随面板拉伸（此前 1fr 列在宽面板里左右空隙巨大） */
  display: grid;
  grid-template-columns: repeat(8, 36px);
  justify-content: start;
  gap: 1px 2px;
}
.cell {
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 19px;
  border-radius: 6px;
  cursor: pointer;
  transition: transform 0.1s ease, background 0.1s ease;
}
.cell:hover {
  background: var(--hz-color-surface);
  transform: scale(1.15);
}
.cell:active {
  transform: scale(0.9);
}
.empty {
  padding: 20px 0;
  display: flex;
  justify-content: center;
}
.empty-text {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 窄屏 6 列（拇指命中率优先） */
@media (max-width: 480px) {
  .grid {
    grid-template-columns: repeat(6, 40px);
  }
  .cell {
    height: 36px;
    font-size: 21px;
  }
}
</style>
