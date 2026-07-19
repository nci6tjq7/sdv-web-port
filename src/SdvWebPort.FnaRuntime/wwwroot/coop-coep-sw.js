// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// Based on the coi-serviceworker pattern (https://github.com/gzuidhof/coi-serviceworker)
//
// Critical features:
// 1. only-if-cached guard — .NET runtime's modulepreload requests crash otherwise
// 2. CORP: cross-origin on ALL responses (including catch fallback) — COEP requires CORP
// 3. Opaque responses (status === 0) passed through unchanged
// 4. Bump CACHE_NAME to force SW update on existing clients

const CACHE_NAME = 'sdv-coop-coep-v5';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

// Helper: create a new Response with COOP/COEP/CORP headers added
function withCoopCoepHeaders(response) {
    if (response.status === 0) {
        // Opaque response — can't read body, pass through unchanged
        return response;
    }
    const newHeaders = new Headers(response.headers);
    newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
    newHeaders.set('Cross-Origin-Embedder-Policy', 'require-corp');
    newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');
    return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers: newHeaders,
    });
}

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

    // CRITICAL: Skip /deps/ requests! Sync XMLHttpRequest (used by
    // TitleContainer.OpenStream to fetch Content files) cannot read
    // SW-wrapped responses. Let these requests go directly to the network.
    if (req.url.includes('/deps/')) {
        return;
    }

    event.respondWith(
        fetch(req)
            .then((response) => withCoopCoepHeaders(response))
            .catch(() => {
                // Fallback: re-fetch and STILL add CORP header (COEP requires it)
                return fetch(req).then((response) => withCoopCoepHeaders(response));
            })
    );
});
