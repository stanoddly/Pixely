// Checks the offline worker of a publish with PixelyBrowserOffline in Chromium: it installs after the first frame, a reload is answered
// from its cache, and once the server has no assets list, as after a publish without the property, it retires: the page loads again
// from the network, the registration is gone and so are its caches. With --nested the same publish is also served at /proj/, a site
// inside the scope of the one at /, as on one GitHub Pages account; retiring the worker at / must keep the caches and the worker of
// /proj/. One server on one port serves every step, so the origin stays the same.
//
// usage: node run-offline.js <wwwroot> [--nested] [--register] [--headless]
//
// main.js registers the worker after the first frame. --register registers it from here once the page has loaded, for a publish
// that never draws a frame, such as a package test fixture.
//
// Exit code 0 means every check passed.
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');

const options = { nested: false, register: false, headless: false };
const positional = [];
for (let index = 2; index < process.argv.length; index++) {
    const argument = process.argv[index];
    if (argument === '--nested') {
        options.nested = true;
    } else if (argument === '--register') {
        options.register = true;
    } else if (argument === '--headless') {
        options.headless = true;
    } else {
        positional.push(argument);
    }
}
const root = positional[0];
if (!root) {
    console.error('usage: node run-offline.js <wwwroot> [--nested] [--register] [--headless]');
    process.exit(2);
}

const contentTypes = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json', '.webmanifest': 'application/manifest+json' };
// Paths the server answers with 404 although the file exists, and the paths requested since the last reset.
const missing = new Set();
let requested = [];
const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost');
    requested.push(url.pathname);
    if (url.pathname === '/favicon.ico') {
        response.writeHead(204);
        response.end();
        return;
    }
    const relative = options.nested && url.pathname.startsWith('/proj/') ? url.pathname.slice('/proj'.length) : url.pathname;
    const file = path.join(root, decodeURIComponent(relative.endsWith('/') ? `${relative}index.html` : relative));
    if (missing.has(url.pathname) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
        response.writeHead(404);
        response.end();
        return;
    }
    response.writeHead(200, { 'Content-Type': contentTypes[path.extname(file)] ?? 'application/octet-stream' });
    fs.createReadStream(file).pipe(response);
});

const failures = [];
function check(condition, message) {
    console.log(`[${condition ? 'ok' : 'FAIL'}] ${message}`);
    if (!condition) {
        failures.push(message);
    }
}

async function waitFor(page, predicate, argument, timeoutMs = 30000) {
    try {
        await page.waitForFunction(predicate, argument, { timeout: timeoutMs, polling: 250 });
        return true;
    } catch {
        return false;
    }
}

// The worker registers after the first frame and is ready once it has cached every file.
async function waitForActiveWorker(page) {
    if (options.register) {
        await page.evaluate(() => navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }));
    }
    return waitFor(page, async () => (await navigator.serviceWorker.getRegistration())?.active?.state === 'activated');
}

async function cacheNames(page) {
    return page.evaluate(async () => (await caches.keys()).filter(name => name.startsWith('pixely|')));
}

(async () => {
    await new Promise(resolve => server.listen(0, resolve));
    const origin = `http://localhost:${server.address().port}`;
    const browser = await chromium.launch({
        executablePath: process.env.PIXELY_CHROMIUM || undefined,
        headless: options.headless,
        args: ['--enable-features=Vulkan', '--enable-unsafe-webgpu', '--ignore-gpu-blocklist']
    });
    const context = await browser.newContext({ viewport: { width: 640, height: 400 } });
    const page = await context.newPage();
    page.on('console', message => console.log(`[${message.type()}] ${message.text()}`));

    await page.goto(`${origin}/`, { waitUntil: 'load' });
    check(await waitForActiveWorker(page), 'the worker at / installs and activates');
    if (options.nested) {
        await page.goto(`${origin}/proj/`, { waitUntil: 'load' });
        check(await waitForActiveWorker(page), 'the worker at /proj/ installs and activates');
        check((await cacheNames(page)).filter(name => name.endsWith(`|${origin}/proj/`)).length === 1, 'the worker at /proj/ has one cache of its scope');
        await page.goto(`${origin}/`, { waitUntil: 'load' });
    }
    check((await cacheNames(page)).filter(name => name.endsWith(`|${origin}/`)).length === 1, 'the worker at / has one cache of its scope');

    requested = [];
    await page.reload({ waitUntil: 'load' });
    check(await page.evaluate(() => navigator.serviceWorker.controller?.scriptURL), `the reloaded page is controlled (${await page.evaluate(() => navigator.serviceWorker.controller?.scriptURL)})`);
    await page.waitForTimeout(3000);
    const fromNetwork = requested.filter(pathname => !pathname.endsWith('/service-worker.js') && !pathname.endsWith('/service-worker-assets.js') && pathname !== '/favicon.ico');
    check(fromNetwork.length === 0, `the reload fetched nothing else from the network (${fromNetwork.join(', ') || 'nothing'})`);

    // As after a publish without PixelyBrowserOffline: neither the worker nor its list is there.
    missing.add('/service-worker.js');
    missing.add('/service-worker-assets.js');
    requested = [];
    await page.reload({ waitUntil: 'load' });
    check(await waitFor(page, async () => (await navigator.serviceWorker.getRegistrations()).every(registration => registration.scope !== `${location.origin}/`)),
        'the worker at / unregisters itself');
    check(await waitFor(page, origin => caches.keys().then(names => !names.some(name => name.startsWith('pixely|') && name.endsWith(`|${origin}/`))), origin),
        'the caches of / are gone');
    check(await waitFor(page, () => document.readyState === 'complete' && !navigator.serviceWorker.controller), 'the page was loaded again from the network');
    check(requested.includes('/index.html') || requested.includes('/'), 'the page itself came from the network');
    if (options.nested) {
        check((await cacheNames(page)).some(name => name.endsWith(`|${origin}/proj/`)), 'the cache of /proj/ is kept');
        check(await page.evaluate(async () => (await navigator.serviceWorker.getRegistrations()).some(registration => registration.scope.endsWith('/proj/'))), 'the worker at /proj/ is kept');
    }

    await browser.close();
    server.close();
    if (failures.length > 0) {
        console.error(`FAILED\n${failures.map(failure => `  ${failure}`).join('\n')}`);
        process.exit(1);
    }
    console.log('PASSED');
})();
