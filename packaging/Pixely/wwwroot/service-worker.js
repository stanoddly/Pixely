// Pixely's offline worker, which a publish with PixelyBrowserOffline serves as service-worker.js and main.js registers after the first
// frame. service-worker-assets.js is the .NET SDK's list of every file of the publish with its SHA-256, compressed copies excepted.
self.importScripts('./service-worker-assets.js');

// Every site on an origin shares Cache Storage, as the project sites of one GitHub Pages account do, and scopes nest, so a cache is
// named by the scope it belongs to and compared by it exactly. The version is the list's own hash: a project can set the SDK's.
const scope = self.registration.scope;
const cacheNamePrefix = 'pixely|';
// The SDK writes StaticWebAssetBasePath in front of every URL of the list, while the worker is served beneath it. Every publish lists
// the runtime at _framework/dotnet.js below that path, so the shortest URL ending there gives the prefix to strip.
const dotnetJs = '_framework/dotnet.js';
const basePath = self.assetsManifest.assets.map(asset => asset.url).filter(url => url === dotnetJs || url.endsWith(`/${dotnetJs}`))
    .reduce((shortest, url) => shortest === undefined || url.length < shortest.length ? url : shortest, undefined)?.slice(0, -dotnetJs.length) ?? '';
const assets = self.assetsManifest.assets.map(asset => ({
    url: new URL(asset.url.startsWith(basePath) ? asset.url.slice(basePath.length) : asset.url, scope).href,
    hash: asset.hash,
}));
const assetUrls = new Set(assets.map(asset => asset.url));
const indexUrl = new URL('index.html', scope).href;
const assetsListUrl = new URL('service-worker-assets.js', self.location.href).href;
const cacheNamePromise = hashAssets().then(version => `${cacheNamePrefix}${version}|${scope}`);
// Each cached response carries the hash it was fetched for, so an install reuses what an earlier version cached.
const hashHeader = 'X-Pixely-Asset-Hash';

self.addEventListener('install', event => event.waitUntil(install()));
self.addEventListener('activate', event => event.waitUntil(deleteEarlierCaches()));
self.addEventListener('fetch', onFetch);

// Fills this version's cache. An asset already cached with the same hash, in this cache or one of an earlier version, is not
// downloaded again; the rest is fetched past the HTTP cache's freshness and checked against its hash. The cache is only added to,
// so a new worker of the same version leaves the active worker's cache intact.
async function install() {
    const cacheName = await cacheNamePromise;
    const cache = await caches.open(cacheName);
    const earlierNames = (await ownCacheNames()).filter(name => name !== cacheName);
    await Promise.all(assets.map(async asset => {
        if (await findCached(asset, [cacheName])) {
            return;
        }
        const earlier = await findCached(asset, earlierNames);
        if (earlier) {
            await cache.put(asset.url, earlier);
            return;
        }
        const response = await fetch(new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));
        if (!response.ok) {
            throw new Error(`${asset.url} answered ${response.status}`);
        }
        const headers = new Headers(response.headers);
        headers.set(hashHeader, asset.hash);
        // A new response also drops the redirected flag, which a navigation must not be answered with.
        await cache.put(asset.url, new Response(response.body, { status: response.status, statusText: response.statusText, headers }));
    }));
}

async function findCached(asset, cacheNames) {
    for (const cacheName of cacheNames) {
        const response = await caches.match(asset.url, { cacheName });
        if (response?.headers.get(hashHeader) === asset.hash) {
            return response;
        }
    }
    return undefined;
}

function onFetch(event) {
    const request = event.request;
    if (request.method !== 'GET') {
        return;
    }
    const url = new URL(request.url);
    url.hash = '';
    if (request.mode === 'navigate') {
        // Only the page itself: a worker at / would otherwise answer the pages of every other site on the origin.
        url.search = '';
        if (url.href === scope || url.href === indexUrl) {
            const served = serveIndex(request);
            event.respondWith(served.then(result => result.response));
            event.waitUntil(served.then(result => result.fromCache ? retireIfUnpublished(event.resultingClientId) : undefined));
        }
        return;
    }
    if (assetUrls.has(url.href)) {
        event.respondWith(serveAsset(request));
    }
}

// caches.match never creates a cache, so a page still controlled after a retirement cannot bring a deleted one back.
async function serveIndex(request) {
    const cached = await caches.match(indexUrl, { cacheName: await cacheNamePromise });
    return cached ? { response: cached, fromCache: true } : { response: await fetch(request), fromCache: false };
}

async function serveAsset(request) {
    return await caches.match(request, { cacheName: await cacheNamePromise }) ?? fetch(request);
}

// A publish without PixelyBrowserOffline, or a build served on the same origin, has no assets list. The browser's update check then
// fails and keeps this worker, so the worker checks the list itself whenever it served the page from its cache. A missing file is a
// 4xx (S3 answers 403) or, from a server that falls back to index.html, a page; a timeout, a rate limit, a 5xx and no network leave
// the worker in place. Unregistering takes effect at once, so the page this navigation created is loaded again from the network, and
// any other page reaches the network on its next navigation without being interrupted.
async function retireIfUnpublished(resultingClientId) {
    let response;
    try {
        response = await fetch(assetsListUrl, { cache: 'no-cache' });
    } catch {
        return;
    }
    const page = response.headers.get('Content-Type')?.toLowerCase().startsWith('text/html');
    await response.body?.cancel();
    const missing = response.status >= 400 && response.status < 500 && response.status !== 408 && response.status !== 429;
    if (!missing && !(response.ok && page)) {
        return;
    }
    await self.registration.unregister();
    const client = resultingClientId ? await self.clients.get(resultingClientId) : undefined;
    await client?.navigate(client.url).catch(() => {});
    await deleteAllCaches();
}

async function deleteEarlierCaches() {
    const cacheName = await cacheNamePromise;
    await Promise.all((await ownCacheNames()).filter(name => name !== cacheName).map(name => caches.delete(name)));
}

async function deleteAllCaches() {
    await Promise.all((await ownCacheNames()).map(name => caches.delete(name)));
}

async function ownCacheNames() {
    return (await caches.keys()).filter(name => {
        if (!name.startsWith(cacheNamePrefix)) {
            return false;
        }
        const end = name.indexOf('|', cacheNamePrefix.length);
        return end !== -1 && name.slice(end + 1) === scope;
    });
}

// The list is sorted by URL, so its pairs hash the same way every time.
async function hashAssets() {
    const text = self.assetsManifest.assets.map(asset => `${asset.url} ${asset.hash}`).join('\n');
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text)));
    return Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join('');
}
