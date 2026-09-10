// Asks what a browser supports with NO command line flags, to find out whether the
// browser build needs a specially launched browser or runs on a stock one.
// JSPI and WebGPU are the two things the build depends on.
const { chromium } = require('playwright');
const http = require('http');

const server = http.createServer((_, res) => {
    res.writeHead(200, { 'Content-Type': 'text/html' });
    res.end('<!doctype html><title>vanilla check</title><body>probe</body>');
});

const candidates = [
    { name: 'headless shell, NO flags', opts: {} },
    { name: 'full chromium, headless, NO flags', opts: { channel: 'chromium' } },
    { name: 'full chromium, headed, NO flags', opts: { channel: 'chromium', headless: false } },
];

(async () => {
    await new Promise(r => server.listen(8620, r));
    for (const c of candidates) {
        let browser;
        try {
            browser = await chromium.launch(c.opts);
            const page = await browser.newPage();
            await page.goto('http://localhost:8620/');
            const result = await page.evaluate(async () => {
                const out = {};
                out.version = navigator.userAgent.match(/Chrome\/[\d.]+/)?.[0] ?? '?';
                // JSPI: both halves of the API must exist
                out.jspi = (typeof WebAssembly.Suspending === 'function')
                    && (typeof WebAssembly.promising === 'function');
                if (!navigator.gpu) {
                    out.webgpu = 'navigator.gpu MISSING';
                    return out;
                }
                const adapter = await navigator.gpu.requestAdapter();
                if (!adapter) {
                    out.webgpu = 'requestAdapter -> null';
                    return out;
                }
                const info = adapter.info ?? {};
                out.webgpu = `adapter ok vendor=${info.vendor ?? '?'} arch=${info.architecture ?? '?'} desc=${info.description ?? '?'}`;
                return out;
            });
            const verdict = (result.jspi && String(result.webgpu).startsWith('adapter ok')) ? 'PASS' : 'FAIL';
            console.log(`${verdict}  ${c.name}`);
            console.log(`      ${result.version}  JSPI=${result.jspi}  WebGPU=${result.webgpu}`);
        } catch (e) {
            console.log(`ERROR ${c.name}\n      ${String(e).split('\n')[0]}`);
        } finally {
            if (browser) { await browser.close(); }
        }
    }
    server.close();
})();
