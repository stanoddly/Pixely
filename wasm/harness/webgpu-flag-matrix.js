// Isolates which flag actually turns WebGPU on here, to tell apart
// "our app needs a specially launched browser" from
// "Chrome gates WebGPU behind Vulkan on this Linux box".
const { chromium } = require('playwright');
const http = require('http');

const server = http.createServer((_, res) => {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end('<!doctype html><title>flag matrix</title><body>probe</body>');
});

const flagSets = [
    [],
    ['--enable-unsafe-webgpu'],
    ['--enable-features=Vulkan'],
    ['--enable-unsafe-webgpu', '--enable-features=Vulkan'],
    ['--enable-features=Vulkan', '--use-angle=vulkan'],
];

(async () => {
    await new Promise(r => server.listen(8621, r));
    for (const headless of [true, false]) {
        for (const args of flagSets) {
            let browser;
            const label = `${headless ? 'headless' : 'headed  '} ${args.length ? args.join(' ') : '(no flags)'}`;
            try {
                browser = await chromium.launch({ channel: 'chromium', headless, args });
                const page = await browser.newPage();
                await page.goto('http://localhost:8621/');
                const r = await page.evaluate(async () => {
                    if (!navigator.gpu) { return 'navigator.gpu MISSING'; }
                    const a = await navigator.gpu.requestAdapter();
                    if (!a) { return 'requestAdapter -> null'; }
                    const i = a.info ?? {};
                    return `OK ${i.vendor ?? '?'} / ${i.description ?? i.architecture ?? '?'}`;
                });
                console.log(`${r.startsWith('OK') ? 'PASS' : 'FAIL'}  ${label}\n        ${r}`);
            } catch (e) {
                console.log(`ERROR ${label}\n        ${String(e).split('\n')[0]}`);
            } finally {
                if (browser) { await browser.close(); }
            }
        }
    }
    server.close();
})();
