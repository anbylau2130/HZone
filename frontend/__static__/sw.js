/* HZone PWA Service Worker（最小安全策略）：
   - 静态资源（图标/字体/打包产物）cache-first，二次访问离线可用
   - 应用壳（导航请求）网络优先，离线时回退已缓存的 index.html（能打开壳，
     数据自然加载不出——私人日记库的数据全在鉴权 API 后面，本就不该进缓存）
   - /api、/media 等动态请求一律直连，SW 不碰（鉴权头/私有内容绝不缓存）
*/
const CACHE = 'hzone-shell-v1';
const SHELL_URL = '/';

self.addEventListener('install', (event) => {
  event.waitUntil(
    caches
      .open(CACHE)
      .then((c) => c.addAll(['/', '/manifest.webmanifest', '/icons/icon-192.png']))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k !== CACHE).map((k) => caches.delete(k))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener('fetch', (event) => {
  const req = event.request;
  if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.origin !== self.location.origin) return;
  // 私有/动态面：SW 不碰
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/media/')) return;

  // 应用壳：网络优先，离线回缓存壳
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req)
        .then((res) => {
          const copy = res.clone();
          caches.open(CACHE).then((c) => c.put(SHELL_URL, copy));
          return res;
        })
        .catch(() => caches.match(SHELL_URL))
    );
    return;
  }

  // 静态资源（icons/manifest/打包产物）：cache-first
  if (url.pathname.startsWith('/icons/') || url.pathname.startsWith('/assets/') || url.pathname === '/manifest.webmanifest') {
    event.respondWith(
      caches.match(req).then(
        (hit) =>
          hit ||
          fetch(req).then((res) => {
            if (res.ok) {
              const copy = res.clone();
              caches.open(CACHE).then((c) => c.put(req, copy));
            }
            return res;
          })
      )
    );
  }
});
