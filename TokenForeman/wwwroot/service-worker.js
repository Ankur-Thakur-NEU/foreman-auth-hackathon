const CACHE_NAME = 'tokenforeman-pwa-v1';
const SHELL_ASSETS = [
  '/',
  '/index.html',
  '/manifest.json',
  '/config.js',
  '/css/app.css',
  '/js/app.js',
  '/icons/icon-192.png',
  '/icons/icon-512.png',
  '/icons/icon-maskable-192.png',
  '/icons/icon-maskable-512.png',
  '/icons/apple-touch-icon.png',
  '/icons/favicon.ico'
];

self.addEventListener('install', event => {
  event.waitUntil((async () => {
    const cache = await caches.open(CACHE_NAME);
    await cache.addAll(SHELL_ASSETS);
    self.skipWaiting();
  })());
});

self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    for (const key of await caches.keys()) {
      if (key !== CACHE_NAME) {
        await caches.delete(key);
      }
    }
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) {
    return;
  }

  if (event.request.mode === 'navigate') {
    event.respondWith((async () => {
      try {
        const response = await fetch(event.request);
        const cache = await caches.open(CACHE_NAME);
        cache.put('/index.html', response.clone());
        return response;
      } catch {
        return caches.match('/index.html');
      }
    })());
    return;
  }

  if (SHELL_ASSETS.includes(url.pathname)) {
    event.respondWith((async () => {
      const cached = await caches.match(event.request);
      if (cached) {
        return cached;
      }
      const response = await fetch(event.request);
      const cache = await caches.open(CACHE_NAME);
      cache.put(event.request, response.clone());
      return response;
    })());
  }
});
