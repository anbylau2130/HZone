<template>
  <view class="page">
    <view class="card">
      <text class="title">HZone</text>
      <text class="sub">{{ $t('app.tagline') }}</text>
    </view>
  </view>
</template>

<style scoped>
.page { min-height: 100vh; background: var(--hz-color-bg); display: flex; align-items: center; justify-content: center; }
.card { background: var(--hz-color-surface); border-radius: var(--hz-radius-lg); padding: 32px; display: flex; flex-direction: column; gap: 8px; align-items: center; }
.title { font-size: 28px; font-weight: 700; color: var(--hz-color-text); }
.sub { font-size: var(--hz-font-size-sm); color: var(--hz-color-text-secondary); }
</style>
