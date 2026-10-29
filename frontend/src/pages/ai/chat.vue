<template>
  <view class="ai-page">
    <!-- 左栏（桌面）：会话列表 + 管理；移动端折叠为顶部条 -->
    <view class="ai-side" :class="{ open: sideOpen }">
      <view class="ai-side-head">
        <text class="ai-side-title">{{ $t('ai.title') }}</text>
        <view class="ai-new" @click="newConversation">＋</view>
      </view>
      <scroll-view scroll-y class="ai-conv-list">
        <view
          v-for="c in conversations"
          :key="c.id"
          class="ai-conv"
          :class="{ on: c.id === activeId }"
          @click="switchConversation(c.id)"
        >
          <text class="ai-conv-title">{{ c.title }}</text>
          <text class="ai-conv-del" @click.stop="removeConversation(c)">✕</text>
        </view>
        <view v-if="!conversations.length" class="ai-conv-empty">
          <text class="ai-hint">{{ $t('ai.noConversations') }}</text>
        </view>
      </scroll-view>
      <!-- 配置入口组 -->
      <view class="ai-side-foot">
        <view class="ai-cfg-btn" @click="settingsOpen = 'provider'">
          <text class="ai-cfg-ico">🔑</text>
          <text class="ai-cfg-label">{{ $t('ai.provider') }}</text>
        </view>
        <view class="ai-cfg-btn" @click="settingsOpen = 'skills'">
          <text class="ai-cfg-ico">✨</text>
          <text class="ai-cfg-label">{{ $t('ai.skills') }}</text>
        </view>
        <view class="ai-cfg-btn" @click="settingsOpen = 'mcp'">
          <text class="ai-cfg-ico">🔌</text>
          <text class="ai-cfg-label">MCP</text>
        </view>
      </view>
    </view>

    <!-- 主区 -->
    <view class="ai-main">
      <view class="ai-topbar">
        <view class="ai-burger" @click="sideOpen = !sideOpen">☰</view>
        <text class="ai-conv-name">{{ activeConversation?.title ?? $t('ai.title') }}</text>
        <view class="ai-top-spacer" />
        <view v-if="messages.length" class="ai-save" @click="saveAsJournal">📄 {{ $t('ai.saveAsJournal') }}</view>
      </view>

      <!-- 消息流 -->
      <scroll-view scroll-y class="ai-msgs" :scroll-top="scrollTop" scroll-with-animation>
        <view v-if="!messages.length && !streaming" class="ai-welcome">
          <text class="ai-welcome-ico">💬</text>
          <text class="ai-welcome-text">{{ providerReady ? $t('ai.welcome') : $t('ai.needProvider') }}</text>
          <view v-if="!providerReady" class="ai-welcome-btn" @click="settingsOpen = 'provider'">
            {{ $t('ai.goConfig') }}
          </view>
        </view>

        <view v-for="m in renderable" :key="m.key" class="ai-msg" :class="m.role">
          <!-- 工具条目 -->
          <view v-if="m.role === 'tool'" class="ai-tool">
            <text class="ai-tool-ico">🔧</text>
            <text class="ai-tool-name">{{ m.toolName }}</text>
            <text v-if="m.resultPreview" class="ai-tool-result">{{ m.resultPreview }}</text>
          </view>
          <!-- 用户 / 助手气泡 -->
          <view v-else class="ai-bubble" :class="m.role">
            <text class="ai-bubble-text" :user-select="true">{{ m.content }}</text>
          </view>
        </view>

        <view v-if="streaming && !streamText" class="ai-typing">
          <text class="ai-typing-dot">●</text>
          <text class="ai-typing-dot">●</text>
          <text class="ai-typing-dot">●</text>
        </view>
      </scroll-view>

      <!-- 输入区：技能 chips + 输入框 + 发送 -->
      <view v-if="skills.length" class="ai-skill-row">
        <view
          v-for="s in skills"
          :key="s.id"
          class="ai-skill"
          :class="{ on: activeSkillId === s.id }"
          @click="activeSkillId = activeSkillId === s.id ? null : s.id"
        >
          <text class="ai-skill-ico">{{ s.icon }}</text>
          <text class="ai-skill-name">{{ s.name }}</text>
        </view>
      </view>
      <view class="ai-input-row">
        <textarea
          v-model="draft"
          class="ai-input"
          :placeholder="$t('ai.inputPlaceholder')"
          :maxlength="20000"
          auto-height
          confirm-type="send"
          @confirm="send"
          :disabled="streaming"
        />
        <view class="ai-send" :class="{ off: !canSend || streaming }" @click="send">
          {{ streaming ? '…' : '➤' }}
        </view>
      </view>
    </view>

    <!-- 设置弹层：provider / skills / mcp -->
    <view v-if="settingsOpen" class="ai-overlay" @click="settingsOpen = null">
      <view class="ai-sheet" @click.stop>
        <!-- Provider -->
        <template v-if="settingsOpen === 'provider'">
          <view class="ai-sheet-head">
            <text class="ai-sheet-title">{{ $t('ai.providerTitle') }}</text>
            <text class="ai-cancel" @click="settingsOpen = null">{{ $t('common.cancel') }}</text>
          </view>
          <text class="ai-field-label">Base URL</text>
          <input v-model="providerForm.baseUrl" class="ai-field" placeholder="https://api.xxx.com/v4" />
          <text class="ai-field-label">API Key{{ provider.hasKey ? `（${$t('ai.keySaved')}）` : '' }}</text>
          <input v-model="providerForm.apiKey" class="ai-field" :placeholder="provider.hasKey ? '********' : 'sk-...'" />
          <text class="ai-field-label">{{ $t('ai.model') }}</text>
          <input v-model="providerForm.model" class="ai-field" placeholder="glm-4.7 / deepseek-chat / gpt-4o-mini" />
          <view class="ai-sheet-actions">
            <button class="ai-mini ghost" @click="testProvider">{{ testing ? '…' : $t('ai.testConn') }}</button>
            <button class="ai-mini" :disabled="testing" @click="saveProvider">{{ $t('common.confirm') }}</button>
          </view>
          <text v-if="testResult" class="ai-test-result" :class="{ ok: testResult.ok }">{{ testResult.text }}</text>
        </template>

        <!-- Skills -->
        <template v-if="settingsOpen === 'skills'">
          <view class="ai-sheet-head">
            <text class="ai-sheet-title">{{ $t('ai.skillsTitle') }}</text>
            <text class="ai-cancel" @click="settingsOpen = null">{{ $t('common.cancel') }}</text>
          </view>
          <scroll-view scroll-y class="ai-skill-list">
            <view v-for="s in skills" :key="s.id" class="ai-skill-row-item">
              <view class="ai-skill-item-mid">
                <text class="ai-skill-item-name">{{ s.icon }} {{ s.name }}</text>
                <text class="ai-skill-item-prompt">{{ s.prompt.slice(0, 60) }}{{ s.prompt.length > 60 ? '…' : '' }}</text>
              </view>
              <text class="ai-skill-item-op" @click="editSkill(s)">✎</text>
              <text class="ai-skill-item-op danger" @click="removeSkill(s)">✕</text>
            </view>
          </scroll-view>
          <button class="ai-mini" @click="editSkill(null)">＋ {{ $t('ai.newSkill') }}</button>
        </template>

        <!-- MCP servers -->
        <template v-if="settingsOpen === 'mcp'">
          <view class="ai-sheet-head">
            <text class="ai-sheet-title">{{ $t('ai.mcpTitle') }}</text>
            <text class="ai-cancel" @click="settingsOpen = null">{{ $t('common.cancel') }}</text>
          </view>
          <text class="ai-hint">{{ $t('ai.mcpHint') }}</text>
          <scroll-view scroll-y class="ai-skill-list">
            <view v-for="m in mcpServers" :key="m.id" class="ai-skill-row-item">
              <view class="ai-skill-item-mid">
                <text class="ai-skill-item-name">{{ m.name }}</text>
                <text class="ai-skill-item-prompt">{{ m.url }}</text>
              </view>
              <text class="ai-skill-item-op" @click="testMcp(m)">⚡</text>
              <text class="ai-skill-item-op danger" @click="removeMcp(m)">✕</text>
            </view>
            <view v-if="!mcpServers.length" class="ai-conv-empty">
              <text class="ai-hint">{{ $t('ai.noMcp') }}</text>
            </view>
          </scroll-view>
          <button class="ai-mini" @click="editMcp">＋ {{ $t('ai.newMcp') }}</button>
        </template>
      </view>
    </view>

    <!-- 技能编辑弹层 -->
    <view v-if="skillEditor" class="ai-overlay" @click="skillEditor = null">
      <view class="ai-sheet" @click.stop>
        <view class="ai-sheet-head">
          <text class="ai-sheet-title">{{ skillEditor.id ? $t('ai.editSkill') : $t('ai.newSkill') }}</text>
          <text class="ai-cancel" @click="skillEditor = null">{{ $t('common.cancel') }}</text>
        </view>
        <view class="ai-skill-edit-row">
          <input v-model="skillEditor.icon" class="ai-field icon" maxlength="4" />
          <input v-model="skillEditor.name" class="ai-field" :placeholder="$t('ai.skillName')" maxlength="50" />
        </view>
        <textarea v-model="skillEditor.prompt" class="ai-field area" :placeholder="$t('ai.skillPrompt')" :maxlength="10000" auto-height />
        <view class="ai-sheet-actions">
          <button class="ai-mini" :disabled="!skillEditor.name.trim() || !skillEditor.prompt.trim()" @click="saveSkill">
            {{ $t('common.confirm') }}
          </button>
        </view>
      </view>
    </view>

    <!-- MCP 编辑弹层 -->
    <view v-if="mcpEditor" class="ai-overlay" @click="mcpEditor = null">
      <view class="ai-sheet" @click.stop>
        <view class="ai-sheet-head">
          <text class="ai-sheet-title">{{ $t('ai.newMcp') }}</text>
          <text class="ai-cancel" @click="mcpEditor = null">{{ $t('common.cancel') }}</text>
        </view>
        <text class="ai-field-label">{{ $t('ai.name') }}</text>
        <input v-model="mcpEditor.name" class="ai-field" maxlength="50" />
        <text class="ai-field-label">URL</text>
        <input v-model="mcpEditor.url" class="ai-field" placeholder="https://mcp.example.com/mcp" />
        <text class="ai-field-label">{{ $t('ai.authHeader') }}</text>
        <input v-model="mcpEditor.authHeader" class="ai-field" placeholder="Authorization: Bearer xxx（可选）" />
        <view class="ai-sheet-actions">
          <button class="ai-mini" :disabled="!mcpEditor.name.trim() || !mcpEditor.url.trim()" @click="saveMcp">
            {{ $t('common.confirm') }}
          </button>
        </view>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import {
  getAiProvider, saveAiProvider, testAiProvider,
  listAiSkills, createAiSkill, updateAiSkill, deleteAiSkill,
  listAiMcpServers, createAiMcpServer, deleteAiMcpServer, testAiMcpServer,
  listAiConversations, createAiConversation, deleteAiConversation,
  listAiMessages, streamAiChat,
  type AiSkillInfo, type AiMcpServerInfo, type AiConversationInfo, type AiMessageInfo
} from '../../api/ai'
import { saveChatAsJournal } from '../../api/ai'
import { getToken } from '../../api/client'

const { t } = useI18n()

// ---------- 会话 ----------
const conversations = ref<AiConversationInfo[]>([])
const activeId = ref<string | null>(null)
const messages = ref<AiMessageInfo[]>([])
const sideOpen = ref(false)
const scrollTop = ref(0)

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
  sideOpen.value = false
}

async function switchConversation(id: string) {
  if (id === activeId.value) return
  await openConversation(id)
  sideOpen.value = false
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

// ---------- Provider ----------
const provider = ref({ baseUrl: '', model: '', hasKey: false })
const providerForm = ref({ baseUrl: '', apiKey: '', model: '' })
const settingsOpen = ref<'provider' | 'skills' | 'mcp' | null>(null)
const testing = ref(false)
const testResult = ref<{ ok: boolean; text: string } | null>(null)
const providerReady = computed(() => provider.value.baseUrl && provider.value.model && provider.value.hasKey)

async function loadProvider() {
  try {
    provider.value = await getAiProvider()
    providerForm.value.baseUrl = provider.value.baseUrl
    providerForm.value.model = provider.value.model
    providerForm.value.apiKey = ''
  } catch {
    /* 未配置 */
  }
}

async function saveProvider() {
  try {
    await saveAiProvider({
      baseUrl: providerForm.value.baseUrl.trim(),
      apiKey: providerForm.value.apiKey.trim() || undefined,
      model: providerForm.value.model.trim()
    })
    uni.showToast({ title: t('publish.saved'), icon: 'success' })
    settingsOpen.value = null
    await loadProvider()
  } catch {
    // 请求层已 toast
  }
}

async function testProvider() {
  testing.value = true
  testResult.value = null
  try {
    // 先保存当前表单再测（体验直觉）
    await saveAiProvider({
      baseUrl: providerForm.value.baseUrl.trim(),
      apiKey: providerForm.value.apiKey.trim() || undefined,
      model: providerForm.value.model.trim()
    })
    const res = await testAiProvider()
    testResult.value = res.ok
      ? { ok: true, text: t('ai.testOk', { n: (res.tools ?? []).length }) }
      : { ok: false, text: `${t('ai.testFail')}：${res.error ?? res.body ?? res.status}` }
    await loadProvider()
  } finally {
    testing.value = false
  }
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

async function removeMcp(m: AiMcpServerInfo) {
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
  // 触发 scroll-top 变化滚动到底
  const el = document.querySelector('.ai-msgs')?.querySelector?.('.uni-scroll-view') as HTMLElement | undefined
  if (el) {
    scrollTop.value = el.scrollHeight + 1000
  }
}

async function send() {
  if (!canSend.value || streaming.value) return
  const content = draft.value.trim()
  const convId = activeId.value
  if (!convId) return
  // 无会话时先建
  let cid = convId
  if (!cid) {
    const created = await createAiConversation()
    conversations.value.unshift(created)
    cid = created.id
    activeId.value = cid
  }
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
        lines.push(m.role === 'user' ? `**${t('ai.me')}**` : `**AI**`)
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

onShow(() => {
  if (!getToken()) return
  void loadConversations()
  void loadProvider()
  void loadSkills()
  void loadMcp()
})
</script>

<style scoped>
.ai-page {
  display: flex;
  height: 100vh;
  background: var(--hz-color-bg);
  overflow: hidden;
}
/* 侧栏 */
.ai-side {
  width: 220px;
  flex-shrink: 0;
  background: var(--hz-color-surface);
  border-right: 1px solid var(--hz-color-border);
  display: flex;
  flex-direction: column;
}
.ai-side-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 12px 8px;
}
.ai-side-title {
  font-size: 16px;
  font-weight: 700;
  color: var(--hz-color-text);
}
.ai-new {
  width: 26px;
  height: 26px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 16px;
  cursor: pointer;
}
.ai-conv-list {
  flex: 1;
  min-height: 0;
}
.ai-conv {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 9px 12px;
  cursor: pointer;
}
.ai-conv.on {
  background: var(--hz-color-bg);
}
.ai-conv-title {
  flex: 1;
  min-width: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-conv.on .ai-conv-title {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.ai-conv-del {
  font-size: 12px;
  color: var(--hz-color-text-secondary);
  padding: 2px 4px;
}
.ai-conv-empty {
  padding: 20px 12px;
}
.ai-hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.ai-side-foot {
  border-top: 1px solid var(--hz-color-border);
  padding: 8px 10px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.ai-cfg-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 8px;
  border-radius: var(--hz-radius-md);
  cursor: pointer;
}
.ai-cfg-btn:active {
  background: var(--hz-color-bg);
}
.ai-cfg-ico {
  font-size: 15px;
}
.ai-cfg-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
/* 主区 */
.ai-main {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
}
.ai-topbar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--hz-color-border);
  background: var(--hz-color-surface);
}
.ai-burger {
  display: none;
  font-size: 18px;
  color: var(--hz-color-text);
  cursor: pointer;
}
.ai-conv-name {
  font-size: 15px;
  font-weight: 600;
  color: var(--hz-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-top-spacer {
  flex: 1;
}
.ai-save {
  flex-shrink: 0;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  padding: 3px 12px;
  cursor: pointer;
}
/* 消息流 */
.ai-msgs {
  flex: 1;
  min-height: 0;
  padding: 16px;
  box-sizing: border-box;
}
.ai-welcome {
  padding: 60px 20px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}
.ai-welcome-ico {
  font-size: 44px;
}
.ai-welcome-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text-secondary);
  text-align: center;
}
.ai-welcome-btn {
  margin-top: 6px;
  padding: 8px 22px;
  border-radius: 999px;
  background: var(--hz-color-primary);
  color: #fff;
  font-size: var(--hz-font-size-sm);
  cursor: pointer;
}
.ai-msg {
  display: flex;
  margin-bottom: 12px;
}
.ai-msg.user {
  justify-content: flex-end;
}
.ai-bubble {
  max-width: 76%;
  border-radius: var(--hz-radius-lg);
  padding: 10px 14px;
}
.ai-bubble.user {
  background: var(--hz-color-primary);
}
.ai-bubble.user .ai-bubble-text {
  color: #fff;
}
.ai-bubble.assistant {
  background: var(--hz-color-surface);
  border: 1px solid var(--hz-color-border);
}
.ai-bubble-text {
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
  user-select: text;
  -webkit-user-select: text;
}
.ai-tool {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--hz-color-surface);
  border: 1px dashed var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 6px 12px;
  max-width: 92%;
  flex-wrap: wrap;
}
.ai-tool-ico {
  font-size: 13px;
}
.ai-tool-name {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-primary);
}
.ai-tool-result {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  flex: 1;
  min-width: 0;
}
.ai-typing {
  display: flex;
  gap: 5px;
  padding: 8px 4px;
}
.ai-typing-dot {
  font-size: 9px;
  color: var(--hz-color-text-secondary);
  animation: hz-ai-blink 1.2s infinite;
}
.ai-typing-dot:nth-child(2) { animation-delay: 0.2s; }
.ai-typing-dot:nth-child(3) { animation-delay: 0.4s; }
@keyframes hz-ai-blink {
  0%, 80%, 100% { opacity: 0.25; }
  40% { opacity: 1; }
}
/* 技能行 + 输入 */
.ai-skill-row {
  display: flex;
  gap: 6px;
  padding: 6px 14px 0;
  flex-wrap: wrap;
}
.ai-skill {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 3px 10px;
  border: 1px solid var(--hz-color-border);
  border-radius: 999px;
  background: var(--hz-color-surface);
  cursor: pointer;
  flex-shrink: 0;
}
.ai-skill.on {
  border-color: var(--hz-color-primary);
  background: color-mix(in srgb, var(--hz-color-primary) 10%, transparent);
}
.ai-skill-ico {
  font-size: 13px;
}
.ai-skill-name {
  font-size: 11px;
  color: var(--hz-color-text);
}
.ai-skill.on .ai-skill-name {
  color: var(--hz-color-primary);
  font-weight: 600;
}
.ai-input-row {
  display: flex;
  align-items: flex-end;
  gap: 8px;
  padding: 10px 14px calc(12px + env(safe-area-inset-bottom));
  background: var(--hz-color-surface);
  border-top: 1px solid var(--hz-color-border);
}
.ai-input {
  flex: 1;
  min-height: 40px;
  max-height: 140px;
  padding: 9px 14px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-lg);
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-base);
  color: var(--hz-color-text);
}
.ai-send {
  flex-shrink: 0;
  width: 40px;
  height: 40px;
  border-radius: 50%;
  background: var(--hz-color-primary);
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 17px;
  cursor: pointer;
}
.ai-send.off {
  opacity: 0.45;
}
/* 弹层 */
.ai-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 999;
}
.ai-sheet {
  width: 92%;
  max-width: 480px;
  max-height: 80vh;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg);
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.ai-sheet-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.ai-sheet-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.ai-cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.ai-field-label {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.ai-field {
  height: 38px;
  padding: 0 12px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  background: var(--hz-color-bg);
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.ai-field.icon {
  width: 52px;
  text-align: center;
  flex-shrink: 0;
}
.ai-field.area {
  height: auto;
  min-height: 110px;
  padding: 10px 12px;
}
.ai-sheet-actions {
  display: flex;
  gap: 8px;
}
.ai-mini {
  flex: 1;
  height: 38px;
  line-height: 38px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.ai-mini.ghost {
  background: var(--hz-color-bg);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
.ai-mini[disabled] {
  opacity: 0.55;
}
.ai-test-result {
  font-size: var(--hz-font-size-sm);
  color: #e54d42;
  line-height: 1.5;
  word-break: break-all;
}
.ai-test-result.ok {
  color: var(--hz-color-primary);
}
.ai-skill-list {
  max-height: 300px;
  flex-shrink: 0;
}
.ai-skill-row-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 4px;
  border-bottom: 1px solid var(--hz-color-border);
}
.ai-skill-item-mid {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.ai-skill-item-name {
  font-size: var(--hz-font-size-sm);
  font-weight: 600;
  color: var(--hz-color-text);
}
.ai-skill-item-prompt {
  font-size: 11px;
  color: var(--hz-color-text-secondary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.ai-skill-item-op {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  padding: 4px;
  cursor: pointer;
}
.ai-skill-item-op.danger {
  color: #e54d42;
}
.ai-skill-edit-row {
  display: flex;
  gap: 8px;
}
/* 移动端：侧栏抽屉化 */
@media (max-width: 767px) {
  .ai-side {
    position: fixed;
    left: 0;
    top: 0;
    bottom: 0;
    z-index: 100;
    transform: translateX(-100%);
    transition: transform 0.2s ease;
    width: 76vw;
    max-width: 300px;
  }
  .ai-side.open {
    transform: translateX(0);
    box-shadow: 8px 0 30px rgba(0, 0, 0, 0.15);
  }
  .ai-burger {
    display: block;
  }
}
</style>
