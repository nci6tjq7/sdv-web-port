// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// Based on the coi-serviceworker pattern (https://github.com/gzuidhof/coi-serviceworker)
//
// Critical features:
// 1. only-if-cached guard — .NET runtime's modulepreload requests crash otherwise
// 2. CORP: cross-origin (not same-origin) — more permissive, handles opaque responses
// 3. Opaque responses (status === 0) passed through unchanged
// 4. Bump CACHE_NAME to force SW update on existing clients

const CACHE_NAME = 'sdv-coop-coep-v3';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('fetch', (event) => {
    const req = event.request;

    // Only handle same-origin GET requests
    if (req.method !== 'GET' || !req.url.startsWith(self.location.origin)) {
        return;
    }

    // CRITICAL: .NET runtime issues modulepreload requests with cache='only-if-cached'
    // and mode='no-cors'. Without this guard, the fetch handler crashes.
    if (req.cache === 'only-if-cached' && req.mode !== 'same-origin') {
        return;
    }

    event.respondWith(
        fetch(req)
            .then((response) => {
                // Pass through opaque responses unchanged (they have status 0)
                if (response.status === 0) {
                    return response;
                }

                // Clone the response and add COOP/COEP/CORP headers
                const newHeaders = new Headers(response.headers);
                newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
                newHeaders.set('Cross-Origin-Embedder-Policy', 'require-corp');
                newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');

                return new Response(response.body, {
                    status: response.status,
                    statusText: response.statusText,
                    headers: newHeaders,
                });
            })
            .catch(() => {
                return fetch(req);
            })
    );
});
