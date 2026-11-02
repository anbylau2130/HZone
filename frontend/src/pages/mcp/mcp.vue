<template>
  <view class="page">
    <view class="section">
      <text class="section-title">{{ $t('hub.mcp') }}</text>
      <text class="hint">{{ $t('mcp.intro') }}</text>
    </view>

    <!-- 第一步：PAT（明文仅创建时返回一次，生成后直接填进下方配置） -->
    <view class="section">
      <text class="section-title">{{ $t('mcp.tokenTitle') }}</text>
      <text class="hint">{{ $t('mcp.tokenHint') }}</text>
      <view v-if="token" class="token-box">
        <text class="token-text" :user-select="true">{{ token }}</text>
      </view>
      <text v-if="token" class="hint">{{ $t('pats.tokenOnceNote') }}</text>
      <view class="btn-row">
        <button class="pick" :disabled="busy" @click="generateToken">
          {{ token ? $t('mcp.regenerate') : $t('mcp.generate') }}
        </button>
        <button class="pick ghost" @click="goPats">{{ $t('mcp.managePats') }}</button>
      </view>
    </view>

    <view class="section">
      <view class="section-head">
        <text class="section-title">{{ $t('mcp.httpTitle') }}</text>
        <text class="badge">{{ $t('mcp.recommended') }}</text>
      </view>
      <text class="hint">{{ $t('mcp.httpHint') }}</text>
      <view class="json-box">
        <text class="json-text" :user-select="true">{{ httpJson }}</text>
      </view>
      <button class="pick" @click="copy(httpJson)">{{ $t('common.copy') }}</button>
    </view>

    <view class="section">
      <text class="section-title">{{ $t('mcp.stdioTitle') }}</text>
      <text class="hint">{{ $t('mcp.stdioHint', { path: $t('mcp.repoPath') }) }}</text>
      <view class="json-box">
        <text class="json-text" :user-select="true">{{ stdioJson }}</text>
      </view>
      <button class="pick" @click="copy(stdioJson)">{{ $t('common.copy') }}</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { onShow } from '@dcloudio/uni-app'
import { useI18n } from 'vue-i18n'
import { createPat } from '../../api/endpoints'
import { formatDateTime } from '../../utils/format'
import { setNavTitle } from '../../locale'

const { t } = useI18n()

const TOKEN_PLACEHOLDER = 'hz_xxxxxxxxxxxx'
// dev 下前端 /api 代理随 vite 进程存亡，agent 直连 API 端口更稳；生产走与前端同源的 /api 反代
const apiBase = import.meta.env.DEV ? 'http://localhost:5000' : `${window.location.origin}/api`

const token = ref('')
const busy = ref(false)
const pat = computed(() => token.value || TOKEN_PLACEHOLDER)

const httpJson = computed(() =>
  JSON.stringify(
    {
      mcpServers: {
        hzone: { type: 'http', url: `${apiBase}/mcp`, headers: { Authorization: `Bearer ${pat.value}` } }
      }
    },
    null,
    2
  )
)

const stdioJson = computed(() =>
  JSON.stringify(
    {
      mcpServers: {
        hzone: {
          command: 'node',
          args: [`${t('mcp.repoPath')}/mcp/dist/index.js`],
          env: { HZONE_URL: apiBase, HZONE_PAT: pat.value }
        }
      }
    },
    null,
    2
  )
)

async function generateToken() {
  busy.value = true
  try {
    const created = await createPat(t('mcp.patName', { time: formatDateTime(new Date().toISOString()) }))
    token.value = created.token
  } catch {
    // 请求层已 toast（含活跃 PAT 数量上限 409）
  } finally {
    busy.value = false
  }
}

function copy(text: string) {
  uni.setClipboardData({
    data: text,
    success: () => {
      if (token.value) uni.showToast({ title: t('common.copied'), icon: 'success' })
      else uni.showToast({ title: t('mcp.copiedNeedToken'), icon: 'none' })
    }
  })
}

function goPats() {
  uni.navigateTo({ url: '/pages/pats/pats' })
}

onShow(() => {
  setNavTitle('hub.mcp')
})
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
.section-head {
  display: flex;
  align-items: center;
  gap: 8px;
}
.section-title {
  font-size: var(--hz-font-size-base);
  font-weight: 600;
  color: var(--hz-color-text);
}
.badge {
  font-size: 11px;
  color: var(--hz-color-primary);
  border: 1px solid var(--hz-color-primary);
  border-radius: 999px;
  padding: 1px 8px;
}
.hint {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
  line-height: 1.6;
}
.token-box {
  background: var(--hz-color-bg);
  border: 1px dashed var(--hz-color-primary);
  border-radius: var(--hz-radius-md);
  padding: 10px 12px;
}
.token-text {
  font-family: Consolas, Monaco, monospace;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  word-break: break-all;
  user-select: text;
  -webkit-user-select: text;
}
.json-box {
  background: var(--hz-color-bg);
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 12px;
  overflow-x: auto;
}
.json-text {
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
  line-height: 1.6;
  color: var(--hz-color-text);
  white-space: pre;
  user-select: text;
  -webkit-user-select: text;
}
.btn-row {
  display: flex;
  gap: 8px;
}
.btn-row .pick {
  flex: 1;
}
.pick {
  height: 38px;
  line-height: 38px;
  padding: 0 14px;
  margin: 0;
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
  font-size: var(--hz-font-size-sm);
}
.pick.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
</style>
