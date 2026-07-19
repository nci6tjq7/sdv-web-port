// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// Based on the coi-serviceworker pattern (https://github.com/gzuidhof/coi-serviceworker)
//
// For /deps/ requests: add ONLY CORP header (sync XHR can read this)
// For _framework/ requests: add ONLY CORP header (COEP requires it)
// For navigation (HTML) requests: add COOP + COEP + CORP headers

const CACHE_NAME = 'sdv-coop-coep-v10';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

function withAllHeaders(response) {
    if (response.status === 0) return response;
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

function withCorpOnly(response) {
    if (response.status === 0) return response;
    const newHeaders = new Headers(response.headers);
    newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');
    return new Response(response.body, {
        status: response.status,
        statusText: response.statusText,
        headers: newHeaders,
    });
}

self.addEventListener('fetch', (event) => {
    const req = event.request;

    if (req.method !== 'GET' || !req.url.startsWith(self.location.origin)) {
        return;
    }

    if (req.cache === 'only-if-cached' && req.mode !== 'same-origin') {
        return;
    }

    // For /deps/ requests: add ONLY CORP header
    if (req.url.includes('/deps/')) {
        event.respondWith(
            fetch(req)
                .then((response) => withCorpOnly(response))
                .catch(() => {
                    return fetch(req).then((response) => withCorpOnly(response));
                })
        );
        return;
    }

    // For _framework/ requests: add ONLY CORP header (COEP requires it)
    // Use stream (response.body) for performance — arrayBuffer() buffers
    // entire file in memory, causing hangs on 50MB+ wasm files.
    if (req.url.includes('/_framework/')) {
        event.respondWith(
            fetch(req)
                .then((response) => withCorpOnly(response))
                .catch(() => {
                    return fetch(req).then((response) => withCorpOnly(response));
                })
        );
        return;
    }

    // For navigation (HTML) and other requests: add COOP + COEP + CORP
    event.respondWith(
        fetch(req)
            .then((response) => withAllHeaders(response))
            .catch(() => {
                return fetch(req).then((response) => withAllHeaders(response));
            })
    );
});
