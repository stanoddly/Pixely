import { dotnet } from './_framework/dotnet.js';
// A namespace import keeps a replaced pixely-host.js that predates readDeviceLoss, readException or loadCompressedResource loading; a
// named import would fail to link. Without loadCompressedResource the runtime fetches every file itself.
import * as host from './pixely-host.js';

// The page is the screen; SDL's Emscripten port draws into Module.canvas, the element its default selector "#canvas" names.
const canvas = document.getElementById('canvas');
const loading = showLoading();

// pixely-host.js requests the adapter and runs the first frame while runMain runs, so the listeners go in first.
globalThis.addEventListener('pixelygpuadapter', event => {
    if (event.detail.isFallbackAdapter) {
        showFallbackNotice();
    }
});
globalThis.addEventListener('pixelyfirstframe', () => loading.indicator.remove(), { once: true });

try {
    // runMain resolves with the value the managed Main returns; it rejects when Main throws, which the default OnException does.
    // The loader reads PixelyBrowserBrotli from the boot config that onConfigLoaded hands it, before the runtime downloads any file.
    // onRuntimeInitialized runs once every file the runtime needs to start is in memory.
    const moduleConfig = { canvas, onConfigLoaded: host.configureCompressedResources, onRuntimeInitialized: () => { loading.stage.textContent = 'Starting…'; } };
    const exitCode = await dotnet.withModuleConfig(moduleConfig).withResourceLoader(host.loadCompressedResource).runMain();
    console.log(`Pixely exited with code ${exitCode}`);
    if (exitCode !== 0) {
        // An OnException that handled the failure returns the code; the generated entry point reported the exception before calling it.
        showStopped([describeException(), `Exit code ${exitCode}.`].filter(Boolean).join('\n'));
    }
} catch (error) {
    console.error('Pixely failed', error);
    showStopped(describeException() ?? error?.message ?? String(error));
    throw error;
} finally {
    // An app can end before its first frame, or never run the frame loop.
    loading.indicator.remove();
}

// The canvas stays black while the runtime downloads and the app starts, which takes a while on a first visit, so the page says what
// it waits for. A bar with a percentage would mislead: the runtime counts a file as loaded once its headers arrive, and its total
// grows as further downloads start. A progress element without a value shows activity instead.
function showLoading() {
    const indicator = document.createElement('div');
    indicator.style.cssText = 'position: fixed; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1em; padding: 1em; box-sizing: border-box; color: #fff; font: 16px system-ui, sans-serif; pointer-events: none;';

    const stage = document.createElement('p');
    stage.style.cssText = 'margin: 0;';
    stage.textContent = 'Downloading…';

    const bar = document.createElement('progress');
    bar.style.cssText = 'width: min(20em, 100%);';

    indicator.append(stage, bar);
    document.body.append(indicator);
    return { indicator, stage };
}

// A managed exception reaches the page with its message only, so the type comes from the record BrowserHost.ReportExceptionAsync left.
function describeException() {
    const exception = host.readException?.();
    return exception ? `${exception.type}: ${exception.message}` : null;
}

// A fallback adapter draws in software, so the game crawls, and the console is not where a player looks. The notice says why over
// the canvas without stopping the game, and dismissing it hands the keyboard back to the canvas.
function showFallbackNotice() {
    const notice = document.createElement('div');
    notice.style.cssText = 'position: fixed; top: 1em; left: 50%; transform: translateX(-50%); width: max-content; max-width: calc(100% - 2em); box-sizing: border-box; display: flex; align-items: center; gap: 1em; padding: 0.75em 1em; background: rgba(0, 0, 0, 0.85); color: #fff; font: 16px system-ui, sans-serif;';

    const message = document.createElement('p');
    message.style.cssText = 'margin: 0;';
    message.textContent = 'Your browser isn\'t using your graphics card, so this game runs slowly. To fix it, search the web for "turn on hardware acceleration" for your browser.';

    const button = document.createElement('button');
    button.style.cssText = 'font: inherit; padding: 0.25em 1em;';
    button.textContent = 'Dismiss';
    button.addEventListener('click', () => {
        notice.remove();
        canvas.focus();
    });

    notice.append(message, button);
    document.body.append(notice);
}

// Once the app has ended the canvas keeps its last frame, and the console is not where a player looks, so the page says what happened
// over the canvas. A reload is the only recovery a page can offer after startup: a lost GPU device took every GPU resource with it.
function showStopped(detail) {
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position: fixed; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1em; padding: 1em; box-sizing: border-box; background: rgba(0, 0, 0, 0.85); color: #fff; font: 16px system-ui, sans-serif; text-align: center;';

    const headline = document.createElement('p');
    headline.style.cssText = 'margin: 0; font-size: 1.5em;';
    headline.textContent = host.readDeviceLoss?.() ? 'The graphics device was lost.' : 'Pixely stopped.';

    const message = document.createElement('pre');
    message.style.cssText = 'margin: 0; max-width: 100%; white-space: pre-wrap; opacity: 0.7;';
    message.textContent = detail;

    const button = document.createElement('button');
    button.style.cssText = 'font: inherit; padding: 0.5em 1.5em;';
    button.textContent = 'Refresh the page';
    button.addEventListener('click', () => location.reload());

    overlay.append(headline, message, button);
    document.body.append(overlay);
    button.focus();
}
