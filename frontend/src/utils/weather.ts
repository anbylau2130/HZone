/**
 * 天气采集（homepage-v2）：全浏览器侧直调，禁止服务端代查。
 * 链路：ipwho.is（IP 定位，免 key，返回 city + 经纬度）→ Open-Meteo（免 key，当前天气）
 * → weather_code 映射现象枚举。任一步失败/超时/非 2xx 一律 throw，由上层静默降级为手动选择。
 */

export type WeatherCondition =
  | 'sunny'
  | 'cloudy'
  | 'overcast'
  | 'fog'
  | 'rain'
  | 'snow'
  | 'showers'
  | 'thunderstorm'

export interface WeatherSnapshot {
  city: string
  condition: WeatherCondition
  tempC: number
}

/** 现象选择器顺序（发布页弹层展示序）。 */
export const WEATHER_CONDITIONS: WeatherCondition[] = [
  'sunny',
  'cloudy',
  'overcast',
  'fog',
  'rain',
  'showers',
  'snow',
  'thunderstorm'
]

/** 现象图标（emoji，i18n 中立）。 */
export const CONDITION_ICONS: Record<WeatherCondition, string> = {
  sunny: '☀️',
  cloudy: '⛅',
  overcast: '☁️',
  fog: '🌫️',
  rain: '🌧️',
  showers: '🌦️',
  snow: '❄️',
  thunderstorm: '⛈️'
}

/** Open-Meteo weather_code → 现象枚举（未覆盖码归入多云）。 */
export function mapWeatherCode(code: number): WeatherCondition {
  if (code === 0) return 'sunny'
  if (code <= 2) return 'cloudy'
  if (code === 3) return 'overcast'
  if (code === 45 || code === 48) return 'fog'
  if (code >= 51 && code <= 67) return 'rain' // 含 66/67 冻雨
  if (code >= 71 && code <= 77) return 'snow'
  if (code >= 80 && code <= 82) return 'showers'
  if (code >= 85 && code <= 86) return 'snow' // Open-Meteo 阵雪
  if (code >= 95) return 'thunderstorm'
  return 'cloudy'
}

/** fetch + AbortController 超时（超时/网络错误均 throw）。 */
async function fetchJson<T>(url: string, timeoutMs: number): Promise<T> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), timeoutMs)
  try {
    const res = await fetch(url, { signal: controller.signal })
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    return (await res.json()) as T
  } finally {
    clearTimeout(timer)
  }
}

// 模块级缓存：一次会话只探一次（发布/编辑两模式共用）。
let cached: WeatherSnapshot | null = null
let inflight: Promise<WeatherSnapshot> | null = null

const GEO_TIMEOUT = 3000
const TOTAL_TIMEOUT = 5000

/**
 * 获取当前天气快照。总预算 5s（IP 定位单步 3s，Open-Meteo 用剩余预算）。
 * 成功结果模块级缓存；失败不缓存（下次进入重试）。
 */
export async function getWeather(): Promise<WeatherSnapshot> {
  if (cached) return cached
  if (!inflight) {
    inflight = fetchWeather().then(
      (w) => {
        cached = w
        inflight = null
        return w
      },
      (err) => {
        inflight = null
        throw err
      }
    )
  }
  return inflight
}

async function fetchWeather(): Promise<WeatherSnapshot> {
  const deadline = Date.now() + TOTAL_TIMEOUT
  const geo = await fetchJson<{ city?: string; latitude: number; longitude: number }>(
    'https://ipwho.is/',
    Math.min(GEO_TIMEOUT, TOTAL_TIMEOUT)
  )
  if (typeof geo.latitude !== 'number' || typeof geo.longitude !== 'number') {
    throw new Error('geo: missing coordinates')
  }
  const remaining = deadline - Date.now()
  if (remaining <= 0) throw new Error('weather: total timeout')
  const forecast = await fetchJson<{
    current?: { temperature_2m?: number; weather_code?: number }
  }>(
    `https://api.open-meteo.com/v1/forecast?latitude=${geo.latitude}&longitude=${geo.longitude}&current=temperature_2m,weather_code`,
    remaining
  )
  const current = forecast.current
  if (typeof current?.temperature_2m !== 'number' || typeof current?.weather_code !== 'number') {
    throw new Error('weather: missing current data')
  }
  return {
    city: geo.city ?? '',
    condition: mapWeatherCode(current.weather_code),
    tempC: current.temperature_2m
  }
}
