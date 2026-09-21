// Serves a published browser build, runs it in Chromium with WebGPU on, echoes the page's console, screenshots the canvas,
// presses Escape to quit the app and reports whether it exited cleanly.
//
// usage: node run-page.js <wwwroot> [--screenshot <file>] [--run-ms <ms>] [--headless]
//
// Chromium exposes a hardware WebGPU adapter on Linux only when headed and Vulkan is enabled; headless gets SwiftShader, which
// loses the device after the first presented frame, so headed is the default. Exit code 0 means the page logged no error and
// the app exited with code 0.
const { chromium } = require('playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');

const options = { screenshot: null, runMs: 3000, headless: false };
const positional = [];
for (let index = 2; index < process.argv.length; index++) {
    const argument = process.argv[index];
    if (argument === '--screenshot') {
        options.screenshot = process.argv[++index];
    } else if (argument === '--run-ms') {
        options.runMs = Number(process.argv[++index]);
    } else if (argument === '--headless') {
        options.headless = true;
    } else {
        positional.push(argument);
    }
}
const root = positional[0];
if (!root) {
    console.error('usage: node run-page.js <wwwroot> [--screenshot <file>] [--run-ms <ms>] [--headless]');
    process.exit(2);
}

const contentTypes = { '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.wasm': 'application/wasm', '.json': 'application/json' };
const server = http.createServer((request, response) => {
    const url = new URL(request.url, 'http://localhost');
    // The browser asks for one on its own, and the console would report the 404 as an error.
    if (url.pathname === '/favicon.ico') {
        response.writeHead(204);
        response.end();
        return;
    }
    const file = path.join(root, decodeURIComponent(url.pathname === '/' ? '/index.html' : url.pathname));
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
        response.writeHead(404);
        response.end();
        return;
    }
    response.writeHead(200, { 'Content-Type': contentTypes[path.extname(file)] ?? 'application/octet-stream' });
    fs.createReadStream(file).pipe(response);
});

(async () => {
    await new Promise(resolve => server.listen(0, resolve));
    const origin = `http://localhost:${server.address().port}/`;
    // PIXELY_CHROMIUM names another Chromium executable than the one Playwright installed.
    const browser = await chromium.launch({
        executablePath: process.env.PIXELY_CHROMIUM || undefined,
        headless: options.headless,
        args: ['--enable-features=Vulkan', '--enable-unsafe-webgpu', '--ignore-gpu-blocklist']
    });
    const page = await browser.newPage({ viewport: { width: 640, height: 400 } });
    const failures = [];
    let exitCode = null;
    page.on('console', message => {
        const text = message.text();
        console.log(`[${message.type()}] ${text}`);
        if (message.type() === 'error') {
            failures.push(text);
        }
        const exit = /^Pixely exited with code (\d+)/.exec(text);
        if (exit) {
            exitCode = Number(exit[1]);
        }
    });
    page.on('pageerror', error => {
        console.log(`[pageerror] ${error.message}`);
        failures.push(error.message);
    });
    await page.goto(origin, { waitUntil: 'load' });
    const adapter = await page.evaluate(async () => {
        const adapter = navigator.gpu ? await navigator.gpu.requestAdapter() : null;
        return adapter ? `${adapter.info?.vendor ?? '?'} ${adapter.info?.architecture ?? ''}`.trim() : 'none';
    });
    console.log(`[adapter] ${adapter}`);
    await page.waitForTimeout(options.runMs);
    if (options.screenshot) {
        await page.screenshot({ path: options.screenshot });
        console.log(`[screenshot] ${options.screenshot}`);
    }
    await page.click('#canvas');
    await page.keyboard.press('Escape');
    // The app is disposed, device included, before Main returns and the exit code is logged.
    await page.waitForTimeout(2000);
    await browser.close();
    server.close();
    if (exitCode !== 0) {
        failures.push(`the app did not exit with code 0 (${exitCode ?? 'no exit'})`);
    }
    if (failures.length > 0) {
        console.error(`FAILED\n${failures.map(failure => `  ${failure}`).join('\n')}`);
        process.exit(1);
    }
    console.log('PASSED');
})();
