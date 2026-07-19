// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// Based on the coi-serviceworker pattern (https://github.com/gzuidhof/coi-serviceworker)
//
// GitHub Pages does NOT support _headers files (that's a Netlify feature).
// So the SW must add COOP/COEP/CORP headers to ALL responses itself.
//
// For /deps/ requests: add ONLY CORP header (sync XHR can read this)
// For ALL other requests: add COOP + COEP + CORP headers

const CACHE_NAME = 'sdv-coop-coep-v12';

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

    // For ALL other requests (including _framework/): add COOP + COEP + CORP
    // This is required because GitHub Pages doesn't support _headers files.
    // Use response.body (stream) for performance — do NOT use arrayBuffer()
    // as it buffers entire files in memory and hangs on 50MB+ wasm files.
    event.respondWith(
        fetch(req)
            .then((response) => withAllHeaders(response))
            .catch(() => {
                return fetch(req).then((response) => withAllHeaders(response));
            })
    );
});
