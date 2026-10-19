<script lang="ts">
// H5 专用原生 <input type="color">，uni-app 会把模板里的 <input> 编译成 uni-input（不支持 color），
// 所以用 render 函数绕过，只在 #ifdef H5 块中使用。
import { defineComponent, h } from 'vue'

export default defineComponent({
  name: 'NativeColorInput',
  props: {
    modelValue: { type: String, required: true }
  },
  emits: ['update:modelValue'],
  render() {
    return h('input', {
      type: 'color',
      value: this.modelValue,
      style: {
        width: '40px',
        height: '28px',
        border: 'none',
        padding: '0',
        background: 'transparent',
        cursor: 'pointer'
      },
      onInput: (e: Event) => this.$emit('update:modelValue', (e.target as HTMLInputElement).value)
    })
  }
})
</script>
