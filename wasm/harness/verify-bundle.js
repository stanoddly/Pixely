// Verifies the hand-off bundle by serving it exactly as serve.sh does and loading both
// tutorials on the hardware adapter, which is the configuration the README tells the
// reader to use.
const { chromium } = require('playwright');
const { spawn } = require('child_process');
const path = require('path');

const BUNDLE = 'process.env.PIXELY_WASM_WORK/pixely-browser-bundle';
const PORT = 8700;

(async () => {
    const server = spawn('python3', ['-m', 'http.server', String(PORT)], { cwd: BUNDLE, stdio: 'ignore' });
    await new Promise(r => setTimeout(r, 2000));

    let browser;
    try {
        browser = await chromium.launch({
            channel: 'chromium',
            headless: false,
            args: ['--enable-features=Vulkan'],
        });

        for (const [name, shot] of [['imagetext', 'bundle-imagetext.png'], ['triangle', 'bundle-triangle.png']]) {
            const page = await browser.newPage({ viewport: { width: 900, height: 600 } });
            const errors = [];
            page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
            page.on('requestfailed', r => errors.push(`requestfailed: ${r.url()}`));
            const logs = [];
            page.on('console', m => logs.push(`[${m.type()}] ${m.text()}`));

            await page.goto(`http://localhost:${PORT}/${name}/`, { waitUntil: 'load' });
            await page.waitForTimeout(9000);
            await page.screenshot({ path: path.join(path.dirname(BUNDLE), shot) });

            console.log(`--- ${name} ---`);
            console.log(logs.slice(-6).join('\n') || '(no console output)');
            console.log(errors.length ? `PROBLEMS:\n${errors.join('\n')}` : 'no page errors, no failed requests');
            await page.close();
        }
    } catch (e) {
        console.log('ERROR ' + String(e).split('\n')[0]);
    } finally {
        if (browser) { await browser.close(); }
        server.kill();
    }
})();
