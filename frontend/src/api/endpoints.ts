/**
 * 后端 API 薄封装：全部走 client.ts 的 request（Bearer 自动带上，401 统一跳登录）。
 * DTO 形状与后端 ContentContracts.cs / CommentInboxEndpoints.cs 等一一对应（camelCase）。
 */
import { request, uploadFile } from './client'

// ---------- 内容 ----------

export interface AuthorInfo {
  id: string
  nickname: string
  /** 票 05 接真头像 */
  avatarUrl?: string | null
  /** 签名（moments-wechat 票 01 下发） */
  signature?: string | null
  /** 封面 /media/ 相对路径（moments-wechat 票 01 下发） */
  coverUrl?: string | null
}

export interface RecipientInfo {
  userId: string
  nickname: string
}

export interface MediaItem {
  id: string
  kind: 'image' | 'video' | 'audio' | 'file'
  url: string
  thumbUrl?: string | null
  sizeBytes: number
}

export interface LunarInfo {
  ganzhiYear: string
  monthDay: string
  term: string
}

export interface WeatherInfo {
  city: string | null
  condition: string | null
  tempC: number | null
}

/** 天气快照提交体（采集见 utils/weather.ts）。 */
export interface WeatherSnapshotInput {
  city: string
  condition: string
  tempC: number
}

export interface ContentItem {
  id: string
  kind: 'say' | 'journal'
  author: AuthorInfo
  text?: string | null
  title?: string | null
  markdownBody?: string | null
  recipients: RecipientInfo[]
  createdAt: string
  updatedAt: string
  commentCount: number
  media?: MediaItem[] | null
  lunar?: LunarInfo | null
  weather?: WeatherInfo | null
  /** 标签（作者打的分类标记，整体替换语义）。 */
  tags?: string[] | null
  /** 点赞数与「我是否已赞」（观察者视角）。 */
  likeCount?: number
  likedByMe?: boolean
  /** 心情档位 1~5（随记；未标/笔记为 null）。 */
  moodLevel?: number | null
}

export interface ContentPage {
  items: ContentItem[]
  total: number
  offset: number
  limit: number
}

export type KindFilter = '' | 'say' | 'journal'

/** 拼查询串（跳过空值）。 */
function qs(params: Record<string, string | number | undefined>): string {
  const parts: string[] = []
  for (const [k, v] of Object.entries(params)) {
    if (v === undefined || v === '') continue
    parts.push(`${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
  }
  const s = parts.join('&')
  return s ? `?${s}` : ''
}

export function createSay(
  text: string,
  recipientIds: string[],
  mediaIds?: string[],
  weather?: WeatherSnapshotInput,
  tags?: string[],
  moodLevel?: number
) {
  return request<ContentItem>('/api/contents/says', {
    method: 'POST',
    data: { text, recipientIds, mediaIds: mediaIds ?? [], weather, tags, moodLevel }
  })
}

export function createJournal(
  title: string,
  markdown: string,
  recipientIds: string[],
  weather?: WeatherSnapshotInput,
  tags?: string[]
) {
  return request<ContentItem>('/api/contents/journals', {
    method: 'POST',
    data: { title, markdown, recipientIds, weather, tags }
  })
}

export function getContent(id: string) {
  return request<ContentItem>(`/api/contents/${id}`)
}

/** 只传显式要改的字段（null/undefined = 不修改）；mediaIds/recipientIds/weather 传了即整体替换。moodLevel 0=清除。 */
export function updateContent(
  id: string,
  payload: {
    text?: string
    title?: string
    markdownBody?: string
    mediaIds?: string[]
    recipientIds?: string[]
    weather?: WeatherSnapshotInput
    tags?: string[]
    moodLevel?: number
  }
) {
  return request<ContentItem>(`/api/contents/${id}`, { method: 'PUT', data: { ...payload } })
}

export function deleteContent(id: string) {
  return request<void>(`/api/contents/${id}`, { method: 'DELETE' })
}

/** 点赞切换（再点撤销）。返回最新 { liked, likeCount }。 */
export function toggleLike(id: string) {
  return request<{ liked: boolean; likeCount: number }>(`/api/contents/${id}/like`, {
    method: 'POST',
    data: {}
  })
}

// ---------- 只读分享外链（token 化，给未注册家人看） ----------

export interface ShareLinkInfo {
  id: string
  contentId: string
  contentTitle: string | null
  expiresAt: string
  createdAt: string
  revokedAt: string | null
}

/** 创建分享链（仅作者；days 1-30 默认 7）。明文 token 仅本次响应返回。 */
export function createShareLink(contentId: string, days = 7) {
  return request<{ id: string; token: string; expiresAt: string; url: string }>(
    `/api/contents/${contentId}/share-links`,
    { method: 'POST', data: { days } }
  )
}

export function listMyShareLinks() {
  return request<{ items: ShareLinkInfo[] }>('/api/me/share-links')
}

export function revokeShareLink(id: string) {
  return request<{ ok: boolean }>(`/api/share-links/${id}`, { method: 'DELETE' })
}

/** 时间线窗口/游标：since=首屏"最近一周"下界；before+beforeId=键集游标（并列时间翻页不丢项）。 */
export interface FeedCursor {
  since?: string
  before?: string
  beforeId?: string
}

function listContents(
  path: string,
  kind: KindFilter,
  offset: number,
  limit: number,
  date?: string,
  q?: string,
  tag?: string,
  cursor?: FeedCursor
) {
  return request<ContentPage>(
    `${path}${qs({
      kind,
      offset,
      limit,
      date,
      q: q || undefined,
      tag: tag || undefined,
      since: cursor?.since,
      before: cursor?.before,
      beforeId: cursor?.beforeId
    })}`
  )
}

/** date=yyyy-MM-dd：热力格点格下钻按天过滤；tag=标签筛选；cursor=时间线窗口/游标。 */
export function listMyContents(
  kind: KindFilter,
  offset: number,
  limit: number,
  date?: string,
  tag?: string,
  cursor?: FeedCursor
) {
  return listContents('/api/me/contents', kind, offset, limit, date, undefined, tag, cursor)
}

export function listReceived(kind: KindFilter, offset: number, limit: number, q?: string) {
  return listContents('/api/me/received', kind, offset, limit, undefined, q)
}

export function listUserContents(
  userId: string,
  kind: KindFilter,
  offset: number,
  limit: number
) {
  return listContents(`/api/users/${userId}/contents`, kind, offset, limit)
}

export function searchContents(
  q: string,
  kind: KindFilter,
  offset: number,
  limit: number,
  tag?: string,
  cursor?: FeedCursor
) {
  return request<ContentPage>(
    `/api/search${qs({
      q,
      kind,
      offset,
      limit,
      tag: tag || undefined,
      since: cursor?.since,
      before: cursor?.before,
      beforeId: cursor?.beforeId
    })}`
  )
}

// ---------- 标签（跨时间主题检索） ----------

export interface TagCloudItem {
  tag: string
  count: number
}

/** 我的标签云：全部标签 + 各自内容计数（计数降序）。 */
export function listMyTags() {
  return request<{ items: TagCloudItem[] }>('/api/me/tags')
}

// ---------- 我的统计（首页 ticket 04） ----------

export interface HeatmapCell {
  date: string
  count: number
}

export interface Footprint {
  city: string
  firstAt: string
  /** 打卡那一刻的天气快照（老数据/无天气为 null）：足迹卡展示用。 */
  condition: string | null
  tempC: number | null
}

export interface MeStats {
  totals: {
    says: number
    journals: number
    friends: number
  }
  streak: number
  today: LunarInfo
  /** 近 90 天（含今天）按天计数，缺天补 0，恒 90 项旧→新。 */
  heatmap: HeatmapCell[]
  /** 天气快照城市足迹（同城连续去重），倒序最近在上。 */
  footprints: Footprint[]
  /** 月度回顾（中国区本月）：篇数/有天气的不同城市数/最常天气现象（null 可）。 */
  monthly: {
    says: number
    journals: number
    cities: number
    topCondition: string | null
  }
}

/** GET /me/stats：四卡数字 + 连续记录天数 + 今日农历 + 热力格 + 足迹 + 月度回顾（后端推导）。 */
export function getMyStats() {
  return request<MeStats>('/api/me/stats')
}

// ---------- 年度报告（"你的这一年"数据故事） ----------

export interface YearlyReport {
  years: number[]
  year: number
  empty?: boolean
  sayCount: number
  journalCount: number
  recordedDays: number
  longestStreak: number
  cities: { city: string; count: number }[]
  weather: { condition: string; count: number }[]
  nightCount: number
  latestAt: string | null
  tags: { tag: string; count: number }[]
  imageCount: number
  voiceCount: number
  commentsReceived: number
  likesReceived: number
  longestJournal: { id: string; title: string | null; chars: number; createdAt: string } | null
  highlight: { id: string; snippet: string | null; likes: number; createdAt: string } | null
}

export function getMyReport(year?: number) {
  return request<YearlyReport>(`/api/me/report${qs({ year })}`)
}

// ---------- 心情追踪×天气 ----------

export interface MoodReport {
  days: number
  totalMarked: number
  byDay: { date: string; avg: number; condition: string | null }[]
  byWeather: { condition: string; avg: number; count: number }[]
  insight: { best: string; bestAvg: number; worst: string; worstAvg: number; gap: number } | null
}

export function getMyMoods(days?: number) {
  return request<MoodReport>(`/api/me/moods${qs({ days })}`)
}

// ---------- 全局搜索（随记/笔记/评论 + 组合筛选） ----------

export interface SearchAllHit {
  id: string
  type: 'say' | 'journal' | 'comment'
  contentId: string
  title: string | null
  snippet: string
  author: string
  createdAt: string
  isMine: boolean
  /** 跳转目标内容类型（评论命中 = 其内容类型）。 */
  targetKind: 'say' | 'journal'
}

export interface SearchAllFilters {
  q: string
  kind?: '' | 'say' | 'journal' | 'comment'
  tag?: string
  from?: string
  to?: string
  offset?: number
  limit?: number
}

export function searchAll(f: SearchAllFilters) {
  return request<{ items: SearchAllHit[]; total: number; offset: number; limit: number }>(
    `/api/me/search-all${qs({ q: f.q, kind: f.kind || undefined, tag: f.tag, from: f.from, to: f.to, offset: f.offset, limit: f.limit })}`
  )
}

// ---------- 那年今天 / 互动摘要（homepage-v2 ticket 06） ----------

export interface OnThisDayItem {
  id: string
  kind: 'say' | 'journal'
  title: string | null
  summary: string | null
  year: number
}

/** GET /me/on-this-day：往年同月同日的内容（倒序限 5 条），无则空数组。 */
export function getOnThisDay() {
  return request<OnThisDayItem[]>('/api/me/on-this-day')
}

export interface InteractionSummary {
  comments: number
  mentions: number
}

/** GET /me/interactions/summary：近 7 天我内容收到的评论数 + 我被 @ 的内容数。 */
export function getInteractionSummary() {
  return request<InteractionSummary>('/api/me/interactions/summary')
}

// ---------- 好友 ----------

export interface FriendInfo {
  id: string
  nickname: string
  email: string
  avatarUrl?: string | null
  signature?: string | null
  coverUrl?: string | null
}

export interface FriendRequestItem {
  id: string
  requester?: AuthorInfo
  addressee?: AuthorInfo
}

export interface FriendRequests {
  incoming: FriendRequestItem[]
  outgoing: FriendRequestItem[]
}

export function listFriends() {
  return request<FriendInfo[]>('/api/friends')
}

export function listFriendRequests() {
  return request<FriendRequests>('/api/friends/requests')
}

export function sendFriendRequest(targetUserId: string) {
  return request<{ id: string }>('/api/friends/requests', {
    method: 'POST',
    data: { targetUserId }
  })
}

export function acceptFriendRequest(id: string) {
  return request<{ ok: boolean }>(`/api/friends/requests/${id}/accept`, { method: 'POST' })
}

export function declineFriendRequest(id: string) {
  return request<{ ok: boolean }>(`/api/friends/requests/${id}/decline`, { method: 'POST' })
}

export function removeFriend(userId: string) {
  return request<void>(`/api/friends/${userId}`, { method: 'DELETE' })
}

// ---------- 头像（composer-polish 票 05） ----------

/** POST /me/avatar：multipart 上传图片，服务端压缩存储并更新头像；返回 /media/ 相对路径。 */
export function uploadAvatar(file: string | Blob) {
  return uploadFile<{ avatarUrl: string }>('/api/me/avatar', file)
}

/** DELETE /me/avatar：清空头像 → 204。 */
export function clearAvatar() {
  return request('/api/me/avatar', { method: 'DELETE' })
}

// ---------- 个人资料（moments-wechat 票 01/02） ----------

/** PUT /me/profile：整体替换签名（null = 清空），≤100 字。 */
export function updateMyProfile(signature: string | null) {
  return request<{ signature: string | null }>('/api/me/profile', {
    method: 'PUT',
    data: { signature }
  })
}

/** POST /me/cover：multipart 上传封面图，服务端压缩存储并更新封面；返回 /media/ 相对路径。 */
export function uploadCover(file: string | Blob) {
  return uploadFile<{ coverUrl: string }>('/api/me/cover', file)
}

/** DELETE /me/cover：清空封面 → 204。 */
export function clearCover() {
  return request('/api/me/cover', { method: 'DELETE' })
}

// ---------- 评论 ----------

export interface CommentItem {
  id: string
  contentId: string
  author: AuthorInfo
  text: string
  createdAt: string
}

export function listComments(contentId: string) {
  return request<{ items: CommentItem[]; total: number }>(`/api/contents/${contentId}/comments`)
}

export function createComment(contentId: string, text: string) {
  return request<CommentItem>(`/api/contents/${contentId}/comments`, {
    method: 'POST',
    data: { text }
  })
}

// ---------- 站内信 ----------

export interface InboxItem {
  id: string
  type: 'mentioned' | 'commented' | 'friendRequest'
  contentId?: string | null
  contentKind?: 'say' | 'journal' | null
  previewText?: string | null
  createdAt: string
  readAt?: string | null
}

export function listInbox(offset: number, limit: number) {
  return request<{ items: InboxItem[]; unreadCount: number; total: number }>(
    `/api/inbox${qs({ offset, limit })}`
  )
}

export function getUnreadCount() {
  return request<{ unreadCount: number }>('/api/inbox/unread-count')
}

export function markInboxRead(id: string) {
  return request<{ ok: boolean }>(`/api/inbox/${id}/read`, { method: 'POST' })
}

// ---------- PAT ----------

export interface PatItem {
  id: string
  name: string
  createdAt: string
  revokedAt?: string | null
}

export function listPats() {
  return request<{ items: PatItem[]; total: number }>('/api/pats')
}

export function createPat(name: string) {
  return request<{ id: string; name: string; token: string; createdAt: string }>('/api/pats', {
    method: 'POST',
    data: { name }
  })
}

export function revokePat(id: string) {
  return request<{ ok: boolean }>(`/api/pats/${id}`, { method: 'DELETE' })
}

// ---------- 初始化向导 ----------

let setupStatusCache: boolean | null = null

/** GET /setup/status（模块级缓存：初始化是一次性状态，避免各入口页重复探测）。 */
export async function getSetupStatus(): Promise<boolean> {
  if (setupStatusCache !== null) return setupStatusCache
  const res = await request<{ initialized: boolean }>('/api/setup/status')
  setupStatusCache = res.initialized
  return setupStatusCache
}

/** 初始化完成后更新缓存，避免入口守卫再探测。 */
export function markSetupInitialized() {
  setupStatusCache = true
}

/** POST /setup/admin：创建首个管理员并完成初始化（已初始化时后端返回 409）。 */
export function createAdmin(email: string, password: string, nickname: string) {
  return request<{ id: string; email: string; nickname: string }>('/api/setup/admin', {
    method: 'POST',
    data: { email, password, nickname }
  })
}

// ---------- 导入导出 / 批量 @ ----------

export function batchRecipients(contentIds: string[], addRecipientIds: string[]) {
  return request<{ updated: number }>('/api/contents/batch-recipients', {
    method: 'POST',
    data: { contentIds, addRecipientIds }
  })
}

// ---------- 播放列表（账号级，每用户一张）+ 音频库 ----------

export interface PlaylistItem {
  id: string
  sourceKind: 'media' | 'url'
  title: string
  url: string | null
  mediaAssetId: string | null
  /** media 项的资产文件名（拼 /media/{storedName} 播放路径，前端 blob 换取用）。 */
  mediaStoredName: string | null
  /** 上传时探测的音频元数据（media 项；老数据/外链为 null）：列表行第二行展示。 */
  artist: string | null
  durationSeconds: number | null
  bitrateKbps: number | null
  sizeBytes: number | null
  /** 来源文件名（"文件位置"：上传原文件 / 外链为 null）。 */
  originalFileName: string | null
  /** 歌词原文（lrc；无则 null）：播放器解析滚动。 */
  lyrics: string | null
  /** 归属歌单 id。 */
  playlistId: string | null
  sortOrder: number
  createdAt: string
}

export interface PlaylistInfo {
  id: string
  name: string
  itemCount: number
  createdAt: string
}

export interface AudioAssetInfo {
  id: string
  originalName: string
  storedName: string
  sizeBytes: number
  artist: string | null
  durationSeconds: number | null
  bitrateKbps: number | null
  /** 共享音频（music 根目录，所有人可见可加列表）。 */
  isShared: boolean
  /** 是否本人上传（共享库里区分自己的和别人共享的）。 */
  mine: boolean
  createdAt: string
}

export function listPlaylist(playlistId?: string) {
  return request<{ playlistId: string; playlistName: string; items: PlaylistItem[]; total: number }>(
    `/api/me/playlist${qs({ playlistId })}`
  )
}

// ---------- 多歌单：列表 / 新建 / 改名 / 删除 ----------

export function listPlaylists() {
  return request<{ items: PlaylistInfo[] }>('/api/me/playlists')
}

export function createPlaylist(name: string) {
  return request<PlaylistInfo>('/api/me/playlists', { method: 'POST', data: { name } })
}

export function renamePlaylist(id: string, name: string) {
  return request<{ id: string; name: string }>(`/api/me/playlists/${id}`, {
    method: 'PUT',
    data: { name }
  })
}

export function deletePlaylist(id: string) {
  return request<{ ok: boolean }>(`/api/me/playlists/${id}`, { method: 'DELETE' })
}

// ---------- 记账（个人账本惰性 + 共同账本） ----------

export interface LedgerMemberInfo {
  userId: string
  nickname: string
}

export interface LedgerInfo {
  id: string
  name: string
  isShared: boolean
  ownerUserId: string
  createdAt: string
  members: LedgerMemberInfo[]
}

export interface LedgerEntryItem {
  id: string
  amountCents: number
  kind: 'expense' | 'income'
  category: string
  note: string | null
  entryDate: string
  recorder: { userId: string; nickname: string }
  createdAt: string
}

export interface LedgerMonth {
  ledgerId: string
  ledgerName: string
  from: string
  entries: LedgerEntryItem[]
  summary: {
    expenseCents: number
    incomeCents: number
    byCategory: { category: string; cents: number }[]
  }
}

export function listLedgers() {
  return request<{ items: LedgerInfo[] }>('/api/me/ledgers')
}

export function createLedger(name: string) {
  return request<LedgerInfo>('/api/me/ledgers', { method: 'POST', data: { name } })
}

export function renameLedger(id: string, name: string) {
  return request<{ id: string; name: string }>(`/api/me/ledgers/${id}`, {
    method: 'PUT',
    data: { name }
  })
}

export function deleteLedger(id: string) {
  return request<{ ok: boolean }>(`/api/me/ledgers/${id}`, { method: 'DELETE' })
}

export function inviteLedgerMember(id: string, userId: string) {
  return request<{ ok: boolean }>(`/api/me/ledgers/${id}/members`, {
    method: 'POST',
    data: { userId }
  })
}

export function removeLedgerMember(id: string, userId: string) {
  return request<{ ok: boolean }>(`/api/me/ledgers/${id}/members/${userId}`, { method: 'DELETE' })
}

export function listLedgerEntries(id: string, month?: string) {
  return request<LedgerMonth>(`/api/me/ledgers/${id}/entries${qs({ month })}`)
}

export function createLedgerEntry(
  ledgerId: string,
  payload: {
    amountCents: number
    kind: 'expense' | 'income'
    category: string
    note?: string
    entryDate: string
  }
) {
  return request<LedgerEntryItem>(`/api/me/ledgers/${ledgerId}/entries`, {
    method: 'POST',
    data: payload
  })
}

export function updateLedgerEntry(
  id: string,
  payload: {
    amountCents: number
    kind: 'expense' | 'income'
    category: string
    note?: string
    entryDate: string
  }
) {
  return request<{ id: string }>(`/api/ledger/entries/${id}`, { method: 'PUT', data: payload })
}

export function deleteLedgerEntry(id: string) {
  return request<{ ok: boolean }>(`/api/ledger/entries/${id}`, { method: 'DELETE' })
}

export function addPlaylistItem(payload: {
  sourceKind: 'media' | 'url'
  title?: string
  url?: string
  mediaAssetId?: string
  playlistId?: string
}) {
  return request<PlaylistItem>('/api/me/playlist', { method: 'POST', data: payload })
}

/** 整体重排：完整 id 序列（与现集合一致），服务端按下标重编顺序。 */
export function reorderPlaylist(ids: string[]) {
  return request<{ ok: boolean }>('/api/me/playlist/reorder', { method: 'PUT', data: { ids } })
}

/** 删除音频资产（连物理文件；级联清理所有播放列表引用；仅本人）。 */
export function deleteMediaAsset(id: string) {
  return request<{ ok: boolean }>(`/api/me/media/${id}`, { method: 'DELETE' })
}

export function removePlaylistItem(id: string) {
  return request<{ ok: boolean }>(`/api/me/playlist/${id}`, { method: 'DELETE' })
}

/** 音频库：本人上传的音频资产（新→旧），播放列表「从音频库添加」用。 */
export function listMyAudioAssets() {
  return request<{ items: AudioAssetInfo[]; total: number }>('/api/me/media')
}

/** 资产重命名（仅本人；改显示名，后续新加列表项取新名）。 */
export function renameMediaAsset(id: string, name: string) {
  return request<{ id: string; originalName: string }>(`/api/me/media/${id}/name`, {
    method: 'PUT',
    data: { name }
  })
}

/** 播放列表项重命名（歌名独立快照，与资产名解耦）。 */
export function renamePlaylistItem(id: string, title: string) {
  return request<{ id: string; title: string }>(`/api/me/playlist/${id}/title`, {
    method: 'PUT',
    data: { title }
  })
}

/** 歌词录入/覆盖（空串=清除），lrc 原文整体存取。 */
export function setPlaylistItemLyrics(id: string, lyrics: string) {
  return request<{ id: string; hasLyrics: boolean }>(`/api/me/playlist/${id}/lyrics`, {
    method: 'PUT',
    data: { lyrics }
  })
}

// ---------- 服务器音乐库（admin 配置磁盘目录，全员默认曲库） ----------

export interface ServerAudioFile {
  name: string
  sizeBytes: number
}

export function getServerMusicLibrary() {
  return request<{ musicDir: string | null; files: ServerAudioFile[] }>(
    '/api/me/player/server-library'
  )
}

/** 配置/清除服务器音乐目录（空串清除；仅 admin，目录必须已存在）。 */
export function setServerMusicDir(musicDir: string) {
  return request<{ musicDir: string | null }>('/api/player/music-dir', {
    method: 'PUT',
    data: { musicDir }
  })
}
