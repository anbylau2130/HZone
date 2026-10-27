/**
 * 语音录制（共享）：发布页随记面板与随记页 SayComposer 共用。
 * 点击开始（≤60s 自停/再点提前停，秒数实时），停止回调给出
 * H5 File（opus-in-webm）/App 临时路径。uni-app H5 无
 * getRecorderManager（实测 method not supported）——H5 自封装
 * MediaRecorder，App 端用 uni 原生 RecorderManager（mp3）。
 */
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

export interface VoiceResult {
  /** H5：可直接上传的 File；App：临时文件路径。 */
  file?: File
  /** App：临时路径；H5：blob 预览地址。 */
  previewUrl: string
}

export function useVoiceRecorder(onDone: (r: VoiceResult) => void) {
  const { t } = useI18n()
  const recording = ref(false)
  const seconds = ref(0)
  let timer: ReturnType<typeof setInterval> | null = null
  const MAX_SECONDS = 60

  function startTimer() {
    stopTimer()
    seconds.value = 0
    timer = setInterval(() => {
      seconds.value += 1
      if (seconds.value >= MAX_SECONDS) stop()
    }, 1000)
  }

  function stopTimer() {
    if (timer) {
      clearInterval(timer)
      timer = null
    }
  }

  // #ifdef H5
  let recorder: MediaRecorder | null = null
  let chunks: Blob[] = []

  async function startH5() {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
    const mime = MediaRecorder.isTypeSupported('audio/webm;codecs=opus')
      ? 'audio/webm;codecs=opus'
      : 'audio/webm'
    chunks = []
    recorder = new MediaRecorder(stream, { mimeType: mime })
    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) chunks.push(e.data)
    }
    recorder.onstop = () => {
      stream.getTracks().forEach((tr) => tr.stop())
      const type = recorder?.mimeType?.split(';')[0] || 'audio/webm'
      const blob = new Blob(chunks, { type })
      const ext = type.includes('webm') ? 'webm' : 'm4a'
      const file = new File([blob], `voice-${Date.now()}.${ext}`, { type })
      onDone({ file, previewUrl: URL.createObjectURL(blob) })
      recorder = null
    }
    recorder.start()
  }
  // #endif

  // #ifndef H5
  let rm: UniApp.RecorderManager | null = null

  function ensureRm(): UniApp.RecorderManager {
    if (rm) return rm
    rm = uni.getRecorderManager()
    rm.onStop((res) => {
      stopTimer()
      recording.value = false
      if (res.tempFilePath) onDone({ previewUrl: res.tempFilePath })
    })
    rm.onError(() => {
      stopTimer()
      recording.value = false
      uni.showToast({ title: t('publish.recordPermissionDenied'), icon: 'none' })
    })
    return rm
  }
  // #endif

  /** canStart=false 时拒绝（如已有媒体，语音独占规则由调用方提示）。 */
  async function toggle(canStart = true) {
    if (recording.value) {
      stop()
      return
    }
    if (!canStart) {
      uni.showToast({ title: t('publish.voiceWithMediaOnly'), icon: 'none' })
      return
    }
    recording.value = true
    // #ifdef H5
    try {
      await startH5()
    } catch {
      recording.value = false
      uni.showToast({ title: t('publish.recordPermissionDenied'), icon: 'none' })
      return
    }
    // #endif
    // #ifndef H5
    ensureRm().start({
      format: 'mp3',
      duration: MAX_SECONDS * 1000,
      sampleRate: 16000,
      encodeBitRate: 48000,
    })
    // #endif
    startTimer()
  }

  function stop() {
    stopTimer()
    recording.value = false
    // #ifdef H5
    recorder?.stop()
    // #endif
    // #ifndef H5
    ensureRm().stop()
    // #endif
  }

  return { recording, seconds, toggle, stop }
}
