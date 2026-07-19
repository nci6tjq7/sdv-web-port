// COOP/COEP Service Worker for SharedArrayBuffer support on GitHub Pages
// Based on the coi-serviceworker pattern (https://github.com/gzuidhof/coi-serviceworker)
//
// For /deps/ requests: add ONLY CORP header (sync XHR can read this)
// For navigation (HTML) requests: add COOP + COEP + CORP headers
// For _framework/ requests: pass through WITHOUT interception (avoids "Failed to fetch")

const CACHE_NAME = 'sdv-coop-coep-v9';

self.addEventListener('install', (event) => {
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    event.waitUntil(self.clients.claim());
});

async function withAllHeaders(response) {
    if (response.status === 0) return response;
    // Buffer the body to avoid stream issues (response.body can be null or locked)
    const body = await response.arrayBuffer();
    const newHeaders = new Headers(response.headers);
    newHeaders.set('Cross-Origin-Opener-Policy', 'same-origin');
    newHeaders.set('Cross-Origin-Embedder-Policy', 'require-corp');
    newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');
    return new Response(body, {
        status: response.status,
        statusText: response.statusText,
        headers: newHeaders,
    });
}

async function withCorpOnly(response) {
    if (response.status === 0) return response;
    const body = await response.arrayBuffer();
    const newHeaders = new Headers(response.headers);
    newHeaders.set('Cross-Origin-Resource-Policy', 'cross-origin');
    return new Response(body, {
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
    // (sync XMLHttpRequest CAN read Response with CORP — the issue before
    //  was COOP/COEP headers, not the Response wrapper itself)
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

    // For _framework/ requests: DON'T intercept — let the browser handle them.
    // The .NET runtime downloads many .wasm files from _framework/. Interception
    // causes "Failed to fetch" errors due to Response body streaming issues.
    // Same-origin resources don't need CORP for COEP compliance.
    if (req.url.includes('/_framework/')) {
        return; // Don't call event.respondWith — browser handles natively
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
