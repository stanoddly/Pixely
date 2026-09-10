// Loads a URL in headless chromium, echoes everything the page logs, and
// optionally screenshots it. WebGPU needs the software adapter flags on a
// headless Linux box with no real GPU exposed to the browser.
//
// usage: node run-page.js <url> [screenshot.png] [waitMs]
const { chromium } = require('playwright');

const url = process.argv[2];
const shot = process.argv[3];
const waitMs = Number(process.argv[4] ?? 8000);

(async () => {
    const browser = await chromium.launch({
        // Verified on this box by webgpu-check.js. `--use-webgpu-adapter=swiftshader` is the
        // load-bearing flag: without it requestAdapter() returns null even though
        // navigator.gpu exists. Note WebGPU needs a secure context, so the page must be
        // served over http://localhost; about:blank never exposes navigator.gpu.
        args: [
            '--enable-unsafe-webgpu',
            '--use-webgpu-adapter=swiftshader',
            '--enable-unsafe-swiftshader',
        ],
    });
    const page = await browser.newPage({ viewport: { width: 1280, height: 720 } });

    page.on('console', msg => console.log(`[${msg.type()}] ${msg.text()}`));
    page.on('pageerror', err => console.log(`[pageerror] ${err.message}\n${err.stack ?? ''}`));
    page.on('requestfailed', req => console.log(`[requestfailed] ${req.url()} ${req.failure()?.errorText}`));

    await page.goto(url, { waitUntil: 'load' });

    const gpu = await page.evaluate(async () => {
        if (!navigator.gpu) { return 'navigator.gpu MISSING'; }
        const adapter = await navigator.gpu.requestAdapter();
        return adapter ? `adapter ok: ${JSON.stringify(adapter.info ?? {})}` : 'requestAdapter returned null';
    });
    console.log(`[webgpu] ${gpu}`);

    await page.waitForTimeout(waitMs);

    if (shot) {
        await page.screenshot({ path: shot });
        console.log(`[screenshot] ${shot}`);
    }

    await browser.close();
})();
