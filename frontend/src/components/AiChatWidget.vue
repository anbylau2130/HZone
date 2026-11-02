<template>
  <!-- 全局 AI 对话悬浮球：独立 Vue 岛挂 body（见 boot/ai-chat-widget.ts），登录且未隐藏时显示。
       拖动松手吸附左右边缘；原地点击 = 开关对话面板 -->
  <div v-if="loggedIn && !hidden" ref="ballRef" class="aiw-ball" :style="ballStyle">
    <span class="aiw-ball-ico">💬</span>
  </div>

  <!-- 对话面板 -->
  <div v-if="panelOpen" class="aiw-overlay" @click="panelOpen = false">
    <div class="aiw-panel" @click.stop>
      <div class="aiw-head">
        <div class="aiw-head-title" @click="convListOpen = !convListOpen">
          <span class="aiw-title-text">{{ activeConversation?.title ?? t('ai.title') }}</span>
          <span class="aiw-caret">▾</span>
        </div>
        <div class="aiw-head-acts">
          <span class="aiw-hact" @click="newConversation">＋</span>
          <span v-if="messages.length" class="aiw-hact" @click="saveAsJournal">📄</span>
          <span class="aiw-hact" @click="openManage">⚙</span>
          <span class="aiw-hact" @click="panelOpen = false">✕</span>
        </div>
      </div>
      <!-- 会话下拉 -->
      <div v-if="convListOpen" class="aiw-conv-pop">
        <div v-for="c in conversations" :key="c.id" class="aiw-conv-item" :class="{ on: c.id === activeId }" @click="pickConversation(c.id)">
          <span class="aiw-conv-name">{{ c.title }}</span>
          <span class="aiw-conv-del" @click.stop="removeConversation(c)">✕</span>
        </div>
        <div v-if="!conversations.length" class="aiw-conv-empty">{{ t('ai.noConversations') }}</div>
      </div>

      <!-- 消息流 -->
      <div ref="msgsRef" class="aiw-msgs">
        <div v-if="!messages.length && !streaming" class="aiw-welcome">
          <span class="aiw-welcome-ico">💬</span>
          <span class="aiw-welcome-text">{{ providerReady ? t('ai.welcome') : t('ai.needProvider') }}</span>
          <div v-if="!providerReady" class="aiw-welcome-btn" @click="goSettings">{{ t('ai.goConfig') }}</div>
        </div>

        <div v-for="m in renderable" :key="m.key" class="aiw-msg" :class="m.role">
          <div v-if="m.role === 'tool'" class="aiw-tool">
            <span class="aiw-tool-ico">🔧</span>
            <span class="aiw-tool-name">{{ m.toolName }}</span>
            <span v-if="m.resultPreview" class="aiw-tool-result">{{ m.resultPreview }}</span>
          </div>
          <div v-else class="aiw-bubble" :class="m.role">
            <span class="aiw-bubble-text">{{ m.content }}</span>
          </div>
        </div>

        <div v-if="streaming && !streamText" class="aiw-typing">
          <span class="aiw-typing-dot">●</span>
          <span class="aiw-typing-dot">●</span>
          <span class="aiw-typing-dot">●</span>
        </div>
      </div>

      <!-- 技能 chips + 输入 -->
      <div v-if="skills.length" class="aiw-skill-row">
        <div
          v-for="s in skills"
          :key="s.id"
          class="aiw-skill"
          :class="{ on: activeSkillId === s.id }"
          @click="activeSkillId = activeSkillId === s.id ? null : s.id"
        >
          <span class="aiw-skill-name">{{ s.icon }} {{ s.name }}</span>
        </div>
      </div>
      <div class="aiw-input-row">
        <textarea
          v-model="draft"
          class="aiw-input"
          :placeholder="t('ai.inputPlaceholder')"
          maxlength="20000"
          :disabled="streaming"
          @keydown.enter.exact.prevent="send"
        ></textarea>
        <div class="aiw-send" :class="{ off: !canSend || streaming }" @click="send">
          {{ streaming ? '…' : '➤' }}
        </div>
      </div>
    </div>
  </div>

  <!-- 管理弹层：技能 / MCP / 隐藏悬浮球 -->
  <div v-if="manageOpen" class="aiw-overlay sheet" @click="manageOpen = false">
    <div class="aiw-sheet" @click.stop>
      <div class="aiw-sheet-head">
        <span class="aiw-sheet-title">{{ t('ai.manage') }}</span>
        <span class="aiw-cancel" @click="manageOpen = false">✕</span>
      </div>
      <div class="aiw-sec-title">{{ t('ai.skillsTitle') }}</div>
      <div class="aiw-list">
        <div v-for="s in skills" :key="s.id" class="aiw-list-item">
          <div class="aiw-item-mid">
            <span class="aiw-item-name">{{ s.icon }} {{ s.name }}</span>
            <span class="aiw-item-sub">{{ s.prompt.slice(0, 60) }}{{ s.prompt.length > 60 ? '…' : '' }}</span>
          </div>
          <span class="aiw-item-op" @click="editSkill(s)">✎</span>
          <span class="aiw-item-op danger" @click="removeSkill(s)">✕</span>
        </div>
      </div>
      <button class="aiw-mini" @click="editSkill(null)">＋ {{ t('ai.newSkill') }}</button>
      <div class="aiw-sec-title">{{ t('ai.mcpTitle') }}</div>
      <div class="aiw-hint">{{ t('ai.mcpHint') }}</div>
      <div class="aiw-list">
        <div v-for="m in mcpServers" :key="m.id" class="aiw-list-item">
          <div class="aiw-item-mid">
            <span class="aiw-item-name">{{ m.name }}</span>
            <span class="aiw-item-sub">{{ m.url }}</span>
          </div>
          <span class="aiw-item-op" @click="testMcp(m)">⚡</span>
          <span class="aiw-item-op danger" @click="removeMcp(m)">✕</span>
        </div>
        <div v-if="!mcpServers.length" class="aiw-conv-empty">{{ t('ai.noMcp') }}</div>
      </div>
      <button class="aiw-mini" @click="editMcp">＋ {{ t('ai.newMcp') }}</button>
      <div class="aiw-hide-row" @click="hideBall">🚫 {{ t('ai.hideBall') }}</div>
      <div class="aiw-hint">{{ t('ai.hideBallHint') }}</div>
    </div>
  </div>

  <!-- 技能编辑弹层 -->
  <div v-if="skillEditor" class="aiw-overlay sheet" @click="skillEditor = null">
    <div class="aiw-sheet" @click.stop>
      <div class="aiw-sheet-head">
        <span class="aiw-sheet-title">{{ skillEditor.id ? t('ai.editSkill') : t('ai.newSkill') }}</span>
        <span class="aiw-cancel" @click="skillEditor = null">{{ t('common.cancel') }}</span>
      </div>
      <div class="aiw-edit-row">
        <input v-model="skillEditor.icon" class="aiw-field icon" maxlength="4" />
        <input v-model="skillEditor.name" class="aiw-field" :placeholder="t('ai.skillName')" maxlength="50" />
      </div>
      <textarea v-model="skillEditor.prompt" class="aiw-field area" :placeholder="t('ai.skillPrompt')" maxlength="10000"></textarea>
      <div class="aiw-btn-row">
        <button class="aiw-mini" :disabled="!skillEditor.name.trim() || !skillEditor.prompt.trim()" @click="saveSkill">
          {{ t('common.confirm') }}
        </button>
      </div>
    </div>
  </div>

  <!-- MCP 编辑弹层 -->
  <div v-if="mcpEditor" class="aiw-overlay sheet" @click="mcpEditor = null">
    <div class="aiw-sheet" @click.stop>
      <div class="aiw-sheet-head">
        <span class="aiw-sheet-title">{{ t('ai.newMcp') }}</span>
        <span class="aiw-cancel" @click="mcpEditor = null">{{ t('common.cancel') }}</span>
      </div>
      <div class="aiw-field-label">{{ t('ai.name') }}</div>
      <input v-model="mcpEditor.name" class="aiw-field" maxlength="50" />
      <div class="aiw-field-label">URL</div>
      <input v-model="mcpEditor.url" class="aiw-field" placeholder="https://mcp.example.com/mcp" />
      <div class="aiw-field-label">{{ t('ai.authHeader') }}</div>
      <input v-model="mcpEditor.authHeader" class="aiw-field" placeholder="Authorization: Bearer xxx（可选）" />
      <div class="aiw-btn-row">
        <button class="aiw-mini" :disabled="!mcpEditor.name.trim() || !mcpEditor.url.trim()" @click="saveMcp">
          {{ t('common.confirm') }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  getAiProvider,
  listAiSkills, createAiSkill, updateAiSkill, deleteAiSkill,
  listAiMcpServers, createAiMcpServer, deleteAiMcpServer, testAiMcpServer,
  listAiConversations, createAiConversation, deleteAiConversation,
  listAiMessages, streamAiChat, saveChatAsJournal,
  type AiSkillInfo, type AiMcpServerInfo, type AiConversationInfo, type AiMessageInfo
} from '../api/ai'
import { getToken } from '../api/client'

const { t } = useI18n()

// ---------- 登录态与隐藏开关 ----------
// 独立 Vue 岛拿不到主应用 pinia：登录/登出靠 client.ts 派发的 hz-auth 事件刷新
const loggedIn = ref(!!getToken())
const hidden = ref(uni.getStorageSync('hz_ai_ball_hidden') === '1')
function refreshAuth() {
  loggedIn.value = !!getToken()
}

// ---------- 悬浮球：拖动 + 吸边 ----------
const POS_KEY = 'hz_ai_ball_pos'
const ballRef = ref<HTMLElement | null>(null)
const pos = ref(loadPos())

function loadPos(): { x: number; y: number } {
  try {
    const raw = uni.getStorageSync(POS_KEY) as string
    if (raw) return JSON.parse(raw) as { x: number; y: number }
  } catch {
    /* 用默认位置 */
  }
  return { x: window.innerWidth - 66, y: Math.round(window.innerHeight * 0.55) }
}

const ballStyle = computed(() => ({ left: `${pos.value.x}px`, top: `${pos.value.y}px` }))

let drag: { sx: number; sy: number; ox: number; oy: number; moved: boolean } | null = null

function onDown(e: PointerEvent) {
  drag = { sx: e.clientX, sy: e.clientY, ox: pos.value.x, oy: pos.value.y, moved: false }
  window.addEventListener('pointermove', onMove)
  window.addEventListener('pointerup', onUp)
}
function onMove(e: PointerEvent) {
  if (!drag) return
  const dx = e.clientX - drag.sx
  const dy = e.clientY - drag.sy
  if (Math.abs(dx) + Math.abs(dy) > 6) drag.moved = true
  pos.value = {
    x: Math.min(Math.max(drag.ox + dx, 8), window.innerWidth - 60),
    y: Math.min(Math.max(drag.oy + dy, 8), window.innerHeight - 60)
  }
}
function onUp() {
  window.removeEventListener('pointermove', onMove)
  window.removeEventListener('pointerup', onUp)
  if (!drag) return
  if (drag.moved) {
    // 吸附到较近的左右边缘
    pos.value.x = pos.value.x + 30 < window.innerWidth / 2 ? 12 : window.innerWidth - 62
    uni.setStorageSync(POS_KEY, JSON.stringify(pos.value))
  } else {
    panelOpen.value = !panelOpen.value
  }
  drag = null
}

function bindBall() {
  const el = ballRef.value
  if (!el || el.dataset.aiwBound) return
  el.dataset.aiwBound = '1'
  el.addEventListener('pointerdown', onDown)
}

// ---------- 面板状态 ----------
const panelOpen = ref(false)
const manageOpen = ref(false)
const convListOpen = ref(false)
const loaded = ref(false)

watch(panelOpen, (v) => {
  if (!v) {
    convListOpen.value = false
    return
  }
  if (!loaded.value) {
    loaded.value = true
    void loadAll()
  }
})

async function loadAll() {
  if (!getToken()) return
  await Promise.allSettled([loadConversations(), loadProvider(), loadSkills(), loadMcp()])
}

function openFromOutside() {
  // 命令面板「AI 对话」：唤起面板并确保悬浮球可见
  if (!getToken()) return
  hidden.value = false
  uni.removeStorageSync('hz_ai_ball_hidden')
  panelOpen.value = true
}

function onBallToggle(v: boolean) {
  hidden.value = !v
}

function goSettings() {
  panelOpen.value = false
  uni.navigateTo({ url: '/pages/settings/settings' })
}

function hideBall() {
  hidden.value = true
  uni.setStorageSync('hz_ai_ball_hidden', '1')
  manageOpen.value = false
  panelOpen.value = false
  uni.showToast({ title: t('ai.hideBallHint'), icon: 'none' })
}

// ---------- Provider（只读，用于空态引导；配置在设置页） ----------
const provider = ref({ baseUrl: '', model: '', hasKey: false })
const providerReady = computed(() => provider.value.baseUrl && provider.value.model && provider.value.hasKey)

async function loadProvider() {
  try {
    provider.value = await getAiProvider()
  } catch {
    /* 未配置 */
  }
}

// ---------- 会话 ----------
const conversations = ref<AiConversationInfo[]>([])
const activeId = ref<string | null>(null)
const messages = ref<AiMessageInfo[]>([])

const activeConversation = computed(() => conversations.value.find((c) => c.id === activeId.value) ?? null)

async function loadConversations() {
  const res = await listAiConversations()
  conversations.value = res.items
  if (!activeId.value && res.items.length) {
    await openConversation(res.items[0].id)
  }
}

async function newConversation() {
  const created = await createAiConversation()
  conversations.value.unshift(created)
  await openConversation(created.id)
  convListOpen.value = false
}

async function pickConversation(id: string) {
  convListOpen.value = false
  if (id === activeId.value) return
  await openConversation(id)
}

async function openConversation(id: string) {
  activeId.value = id
  messages.value = []
  try {
    const res = await listAiMessages(id)
    messages.value = res.items
    scrollBottom()
  } catch {
    // 请求层已 toast
  }
}

function removeConversation(c: AiConversationInfo) {
  uni.showModal({
    title: t('ai.deleteConversation'),
    content: c.title,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (m) => {
      if (!m.confirm) return
      await deleteAiConversation(c.id)
      conversations.value = conversations.value.filter((x) => x.id !== c.id)
      if (activeId.value === c.id) {
        activeId.value = null
        messages.value = []
        if (conversations.value.length) await openConversation(conversations.value[0].id)
      }
    }
  })
}

// ---------- 技能 ----------
const skills = ref<AiSkillInfo[]>([])
const activeSkillId = ref<string | null>(null)
const skillEditor = ref<{ id?: string; icon: string; name: string; prompt: string } | null>(null)

async function loadSkills() {
  try {
    skills.value = (await listAiSkills()).items
  } catch {
    /* 空 */
  }
}

function editSkill(s: AiSkillInfo | null) {
  skillEditor.value = s
    ? { id: s.id, icon: s.icon, name: s.name, prompt: s.prompt }
    : { icon: '✨', name: '', prompt: '' }
}

async function saveSkill() {
  const e = skillEditor.value!
  try {
    if (e.id) await updateAiSkill(e.id, { name: e.name, icon: e.icon, prompt: e.prompt })
    else await createAiSkill({ name: e.name, icon: e.icon, prompt: e.prompt })
    skillEditor.value = null
    await loadSkills()
  } catch {
    // 请求层已 toast
  }
}

function removeSkill(s: AiSkillInfo) {
  uni.showModal({
    title: t('ai.deleteSkill'),
    content: s.name,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (m) => {
      if (!m.confirm) return
      await deleteAiSkill(s.id)
      if (activeSkillId.value === s.id) activeSkillId.value = null
      await loadSkills()
    }
  })
}

// ---------- MCP ----------
const mcpServers = ref<AiMcpServerInfo[]>([])
const mcpEditor = ref<{ name: string; url: string; authHeader: string } | null>(null)

async function loadMcp() {
  try {
    mcpServers.value = (await listAiMcpServers()).items
  } catch {
    /* 空 */
  }
}

function editMcp() {
  mcpEditor.value = { name: '', url: '', authHeader: '' }
}

async function saveMcp() {
  const e = mcpEditor.value!
  try {
    await createAiMcpServer({
      name: e.name.trim(),
      url: e.url.trim(),
      authHeader: e.authHeader.trim() || undefined
    })
    mcpEditor.value = null
    await loadMcp()
  } catch {
    // 请求层已 toast
  }
}

function removeMcp(m: AiMcpServerInfo) {
  uni.showModal({
    title: t('ai.deleteMcp'),
    content: m.name,
    confirmText: t('common.confirm'),
    cancelText: t('common.cancel'),
    success: async (r) => {
      if (!r.confirm) return
      await deleteAiMcpServer(m.id)
      await loadMcp()
    }
  })
}

async function testMcp(m: AiMcpServerInfo) {
  uni.showLoading({ title: '...' })
  try {
    const res = await testAiMcpServer(m.id)
    uni.hideLoading()
    if (res.ok) {
      uni.showToast({ title: t('ai.testOk', { n: res.tools?.length ?? 0 }), icon: 'none' })
    } else {
      uni.showToast({ title: `${t('ai.testFail')}：${res.error ?? ''}`.slice(0, 40), icon: 'none' })
    }
  } catch {
    uni.hideLoading()
  }
}

// ---------- 对话 ----------
const draft = ref('')
const streaming = ref(false)
const streamText = ref('')
const activeStreamTools = ref<{ name: string; result?: string }[]>([])
let aborter: { abort: () => void } | null = null
const msgsRef = ref<HTMLElement | null>(null)

const canSend = computed(() => draft.value.trim().length > 0 && !!activeId.value)

/** 渲染序列：user/assistant 气泡 + 工具条（含流式中的）。 */
const renderable = computed(() => {
  const out: { key: string; role: string; content?: string; toolName?: string; resultPreview?: string }[] = []
  for (const m of messages.value) {
    if (m.role === 'user') out.push({ key: m.id, role: 'user', content: m.content })
    else if (m.role === 'assistant' && m.content) out.push({ key: m.id, role: 'assistant', content: m.content })
    else if (m.role === 'tool') {
      out.push({
        key: m.id,
        role: 'tool',
        toolName: m.toolName ?? 'tool',
        resultPreview: m.content.slice(0, 80)
      })
    }
  }
  if (streaming.value) {
    for (let i = 0; i < activeStreamTools.value.length; i++) {
      const tw = activeStreamTools.value[i]
      out.push({ key: `st-${i}`, role: 'tool', toolName: tw.name, resultPreview: tw.result?.slice(0, 80) })
    }
    if (streamText.value) out.push({ key: 'st-text', role: 'assistant', content: streamText.value })
  }
  return out
})

function scrollBottom() {
  void nextTick(() => {
    const el = msgsRef.value
    if (el) el.scrollTop = el.scrollHeight
  })
}

async function send() {
  if (!canSend.value || streaming.value) return
  const content = draft.value.trim()
  if (!activeId.value) return
  draft.value = ''
  messages.value.push({
    id: `local-${Date.now()}`,
    role: 'user',
    content,
    toolCallsJson: null,
    toolCallId: null,
    toolName: null,
    createdAt: new Date().toISOString()
  })
  streaming.value = true
  streamText.value = ''
  activeStreamTools.value = []
  scrollBottom()
  const cid = activeId.value
  aborter = streamAiChat(cid, content, activeSkillId.value, {
    onDelta: (text) => {
      streamText.value += text
      scrollBottom()
    },
    onTool: (payload) => {
      if (payload.phase === 'start') activeStreamTools.value.push({ name: payload.name })
      else if (payload.phase === 'done') {
        const last = activeStreamTools.value[activeStreamTools.value.length - 1]
        if (last) last.result = payload.result
      }
      scrollBottom()
    },
    onDone: async () => {
      streaming.value = false
      aborter = null
      await openConversation(cid)
      await loadConversationsKeep(cid)
    },
    onError: (msg) => {
      streaming.value = false
      aborter = null
      uni.showToast({ title: msg.slice(0, 60), icon: 'none' })
      void openConversation(cid)
    }
  })
}

async function loadConversationsKeep(cid: string) {
  const res = await listAiConversations()
  conversations.value = res.items
  activeId.value = cid
}

function openManage() {
  convListOpen.value = false
  manageOpen.value = true
}

// ---------- 存为笔记 ----------
function saveAsJournal() {
  const dialogMsgs = messages.value.filter((m) => m.role === 'user' || (m.role === 'assistant' && m.content))
  if (!dialogMsgs.length) return
  uni.showActionSheet({
    itemList: [t('ai.saveAll', { n: dialogMsgs.length }), t('ai.saveLast5')],
    success: async (res) => {
      const picks = res.tapIndex === 0 ? dialogMsgs : dialogMsgs.slice(-5)
      const title = activeConversation.value?.title ?? t('ai.chatNote')
      const lines: string[] = [`> ${t('ai.chatNoteMeta', { model: provider.value.model, n: picks.length })}`, '']
      for (const m of picks) {
        lines.push(m.role === 'user' ? `**${t('ai.me')}**` : '**AI**')
        lines.push('')
        lines.push(m.content)
        lines.push('')
      }
      try {
        await saveChatAsJournal(title, lines.join('\n'))
        uni.showToast({ title: t('publish.success'), icon: 'success' })
      } catch {
        // 请求层已 toast
      }
    }
  })
}

// ---------- 生命周期（独立岛：无页面 onShow，首开面板时懒加载） ----------
watch([loggedIn, hidden], () => {
  void nextTick(bindBall)
})

onMounted(() => {
  window.addEventListener('hz-auth', refreshAuth)
  uni.$on('hz-ai-open', openFromOutside)
  uni.$on('hz-ai-ball', onBallToggle)
  void nextTick(bindBall)
})

onUnmounted(() => {
  window.removeEventListener('hz-auth', refreshAuth)
  uni.$off('hz-ai-open', openFromOutside)
  uni.$off('hz-ai-ball', onBallToggle)
  aborter?.abort()
})
</script>

<style scoped>
/* 悬浮球 */
.aiw-ball {
  position: fixed;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  box-shadow: 0 6px 18px rgba(0, 0, 0, 0.28);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1400;
  cursor: pointer;
  touch-action: none;
  user-select: none;
  -webkit-user-select: none;
}
.aiw-ball-ico {
  font-size: 22px;
  line-height: 1;
}
/* 弹层通用 */
.aiw-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  z-index: 1500;
  display: flex;
  align-items: center;
  justify-content: center;
}
.aiw-overlay.sheet {
  z-index: 1600;
}
/* 对话面板 */
.aiw-panel {
  position: relative;
  width: 94%;
  max-width: 420px;
  height: min(72vh, 640px);
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.25);
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.aiw-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--hz-color-border);
  flex-shrink: 0;
}
.aiw-head-title {
  display: flex;
  align-items: center;
  gap: 5px;
  min-width: 0;
  cursor: pointer;
}
.aiw-title-text {
  font-size: 15px;
  font-weight: 600;
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.aiw-caret {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
}
.aiw-head-acts {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}
.aiw-hact {
  font-size: 14px;
  color: var(--hz-color-text-secondary);
  padding: 4px 7px;
  cursor: pointer;
  border-radius: var(--hz-radius-sm);
}
.aiw-hact:hover {
  background: var(--hz-color-bg);
  color: var(--hz-color-text);
}
/* 会话下拉 */
.aiw-conv-pop {
  position: absolute;
  top: 45px;
  left: 0;
  right: 0;
  max-height: 250px;
  overflow-y: auto;
  background: var(--hz-color-surface);
  border-bottom: 1px solid var(--hz-color-border);
  box-shadow: 0 10px 24px rgba(0, 0, 0, 0.14);
  z-index: 5;
}
.aiw-conv-item {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 9px 14px;
  cursor: pointer;
}
.aiw-conv-item.on {
  background: var(--hz-color-bg);
}
.aiw-conv-name {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.aiw-conv-item.on .aiw-conv-name {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.aiw-conv-del {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
  padding: 2px 4px;
}
.aiw-conv-empty {
  padding: 18px 14px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
/* 消息流 */
.aiw-msgs {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 14px;
}
.aiw-welcome {
  padding: 46px 16px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}
.aiw-welcome-ico {
  font-size: 40px;
}
.aiw-welcome-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  text-align: center;
}
.aiw-welcome-btn {
  margin-top: 4px;
  padding: 8px 22px;
  border-radius: 999px;
  background: var(--hz-color-primary);
  color: #fff;
  font-size: var(--hz-font-size-sm);
  cursor: pointer;
}
.aiw-msg {
  display: flex;
  margin-bottom: 10px;
}
.aiw-msg.user {
  justify-content: flex-end;
}
.aiw-bubble {
  max-width: 78%;
  border-radius: var(--hz-radius-lg);
  padding: 9px 13px;
}
.aiw-bubble.user {
  background: var(--hz-color-primary);
}
.aiw-bubble.user .aiw-bubble-text {
  color: #fff;
}
.aiw-bubble.assistant {
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
}
.aiw-bubble-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.6;
  white-space: pre-wrap;
  word-break: break-word;
  user-select: text;
  -webkit-user-select: text;
}
.aiw-tool {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--hz-color-bg);
  border: 1px dashed var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 6px 12px;
  max-width: 92%;
  flex-wrap: wrap;
}
.aiw-tool-ico {
  font-size: 13px;
}
.aiw-tool-name {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-primary);
}
.aiw-tool-result {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}
.aiw-typing {
  display: flex;
  gap: 5px;
  padding: 8px 4px;
}
.aiw-typing-dot {
  font-size: 9px;
  color: var(--hz-color-text-secondary);
  animation: hz-aiw-blink 1.2s infinite;
}
.aiw-typing-dot:nth-child(2) {
  animation-delay: 0.2s;
}
.aiw-typing-dot:nth-child(3) {
  animation-delay: 0.4s;
}
@keyframes hz-aiw-blink {
  0%,
  80%,
  100% {
    opacity: 0.25;
  }
  40% {
    opacity: 1;
  }
}
/* 技能 chips + 输入 */
.aiw-skill-row {
  display: flex;
  gap: 6px;
  padding: 6px 12px 0;
  flex-wrap: wrap;
  flex-shrink: 0;
}
.aiw-skill {
  display: flex;
  align-items: center;
  padding: 3px 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  cursor: pointer;
  flex-shrink: 0;
}
.aiw-skill.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.aiw-skill-name {
  font-size: 11px;
  color: var(--hz-color-text);
}
.aiw-skill.on .aiw-skill-name {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.aiw-input-row {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 8px 12px calc(10px + env(safe-area-inset-bottom));
  border-top: 1px solid var(--hz-color-border);
  flex-shrink: 0;
}
.aiw-input {
  flex: 1;
  min-height: 38px;
  max-height: 120px;
  padding: 8px 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  font-family: inherit;
  resize: none;
  box-sizing: border-box;
}
.aiw-send {
  flex-shrink: 0;
  width: 38px;
  height: 38px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  cursor: pointer;
}
.aiw-send.off {
  opacity: 0.45;
}
/* 管理弹层 / 编辑弹层 */
.aiw-sheet {
  width: 92%;
  max-width: 460px;
  max-height: 80vh;
  overflow-y: auto;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.25);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.aiw-sheet-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.aiw-sheet-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.aiw-cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
  padding: 2px 6px;
}
.aiw-sec-title {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text-secondary);
  margin-top: 4px;
}
.aiw-hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.aiw-list {
  display: flex;
  flex-direction: column;
}
.aiw-list-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 2px;
  border-bottom: 1px solid var(--hz-color-border);
}
.aiw-item-mid {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.aiw-item-name {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text);
}
.aiw-item-sub {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.aiw-item-op {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 4px;
  cursor: pointer;
}
.aiw-item-op.danger {
  color: #e54d42;
}
.aiw-hide-row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 10px;
  margin-top: 4px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  cursor: pointer;
}
.aiw-btn-row {
  display: flex;
  gap: 8px;
}
.aiw-mini {
  flex: 1;
  height: 38px;
  line-height: 38px;
  font-size: var(--hz-font-size-sm);
  font-family: inherit;
  background: var(--hz-color-primary);
  color: #fff;
  border: none;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.aiw-mini[disabled] {
  opacity: 0.55;
  cursor: default;
}
.aiw-field-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.aiw-field {
  height: 38px;
  padding: 0 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  font-family: inherit;
  box-sizing: border-box;
}
.aiw-field.icon {
  width: 52px;
  text-align: center;
  flex-shrink: 0;
}
.aiw-field.area {
  height: auto;
  min-height: 110px;
  padding: 10px 12px;
  resize: none;
}
.aiw-edit-row {
  display: flex;
  gap: 8px;
}
/* 移动端：面板改为底部抽屉 */
@media (max-width: 767px) {
  .aiw-overlay {
    align-items: flex-end;
  }
  .aiw-panel {
    width: 100%;
    max-width: none;
    height: 78vh;
    border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  }
}
</style>
