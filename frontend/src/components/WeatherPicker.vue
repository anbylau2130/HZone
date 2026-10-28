<template>
  <!-- 天气选择弹层（发布页日记头/随记面板共用）：现象九宫格 + 城市/温度 + 清除。
       打开时从 :weather 回填草稿；确认 emit confirm，清除 emit clear，自身负责关闭 -->
  <view v-if="visible" class="wp-overlay" @click="close">
    <view class="wp-sheet" @click.stop>
      <view class="wp-head">
        <text class="wp-title">{{ $t('weather.title') }}</text>
        <text class="wp-cancel" @click="close">{{ $t('common.cancel') }}</text>
      </view>
      <view class="wp-grid">
        <view
          v-for="c in WEATHER_CONDITIONS"
          :key="c"
          class="wp-cond"
          :class="{ on: draftCondition === c }"
          @click="draftCondition = c"
        >
          <text class="wp-cond-icon">{{ CONDITION_ICONS[c] }}</text>
          <text class="wp-cond-name">{{ $t('weather.conditions.' + c) }}</text>
        </view>
      </view>
      <view class="wp-fields">
        <input v-model="draftCity" class="wp-field" :placeholder="$t('weather.city')" />
        <input v-model="draftTemp" class="wp-field" type="digit" :placeholder="$t('weather.tempPlaceholder')" />
      </view>
      <view class="wp-actions">
        <button class="wp-mini ghost" @click="onClear">{{ $t('weather.clear') }}</button>
        <button class="wp-mini" @click="onConfirm">{{ $t('common.confirm') }}</button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { CONDITION_ICONS, WEATHER_CONDITIONS, type WeatherSnapshot } from '../utils/weather'

const props = defineProps<{ visible: boolean; weather: WeatherSnapshot | null }>()
const emit = defineEmits<{
  (e: 'update:visible', v: boolean): void
  (e: 'confirm', w: WeatherSnapshot): void
  (e: 'clear'): void
}>()

const draftCondition = ref<WeatherSnapshot['condition']>('sunny')
const draftCity = ref('')
const draftTemp = ref('')

// 每次打开从当前快照回填草稿（取消不落）
watch(
  () => props.visible,
  (open) => {
    if (!open) return
    const w = props.weather
    draftCondition.value = w?.condition ?? 'sunny'
    draftCity.value = w?.city ?? ''
    draftTemp.value = w ? String(Math.round(w.tempC)) : ''
  }
)

function close() {
  emit('update:visible', false)
}

function onConfirm() {
  const temp = Number.parseFloat(draftTemp.value)
  emit('confirm', {
    city: draftCity.value.trim(),
    condition: draftCondition.value,
    tempC: Number.isFinite(temp) ? temp : 0
  })
  close()
}

function onClear() {
  emit('clear')
  close()
}
</script>

<style scoped>
.wp-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.45);
  display: flex;
  align-items: flex-end;
  justify-content: center;
  z-index: 999;
}
.wp-sheet {
  width: 100%;
  max-width: 520px;
  background: var(--hz-color-surface);
  border-radius: var(--hz-radius-lg) var(--hz-radius-lg) 0 0;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.wp-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.wp-title {
  font-size: 16px;
  font-weight: 600;
  color: var(--hz-color-text);
}
.wp-cancel {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text-secondary);
}
.wp-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 8px;
}
.wp-cond {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 2px;
  padding: 8px 0;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
}
.wp-cond.on {
  border-color: var(--hz-color-primary);
  background: var(--hz-color-bg);
}
.wp-cond-icon {
  font-size: 22px;
}
.wp-cond-name {
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
}
.wp-fields {
  display: flex;
  gap: 8px;
}
.wp-field {
  flex: 1;
  height: 38px;
  border: 1px solid var(--hz-color-border);
  border-radius: var(--hz-radius-md);
  padding: 0 12px;
  font-size: var(--hz-font-size-sm);
  color: var(--hz-color-text);
  background: var(--hz-color-bg);
}
.wp-actions {
  display: flex;
  gap: 8px;
}
.wp-mini {
  flex: 1;
  height: 38px;
  line-height: 38px;
  font-size: var(--hz-font-size-sm);
  background: var(--hz-color-primary);
  color: #fff;
  border-radius: var(--hz-radius-md);
}
.wp-mini.ghost {
  background: var(--hz-color-surface);
  color: var(--hz-color-text-secondary);
  border: 1px solid var(--hz-color-border);
}
</style>
