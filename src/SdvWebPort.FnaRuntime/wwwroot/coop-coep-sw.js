// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// GitHub Pages doesn't support custom headers, so we use a service worker
// to add Cross-Origin-Opener-Policy and Cross-Origin-Embedder-Policy headers.

const CACHE_NAME = 'sdv-coop-coep-v1';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
    // Only handle same-origin GET requests
    if (event.request.method !== 'GET' || !event.request.url.startsWith(self.location.origin)) {
        return;
    }

    event.respondWith(
        fetch(event.request)
            .then((response) => {
                // Clone the response and add COOP/COEP headers
                const newHeaders = new Headers(response.headers);
                newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
                newHeaders.set('Cross-Origin-Embedder-Policy', 'require-corp');

                return new Response(response.body, {
                    status: response.status,
                    statusText: response.statusText,
                    headers: newHeaders,
                });
            })
            .catch(() => {
                return fetch(event.request);
            })
    );
});
