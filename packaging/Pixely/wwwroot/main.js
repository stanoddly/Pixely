import { dotnet } from './_framework/dotnet.js';
import { readDeviceLoss } from './pixely-host.js';

// The page is the screen; SDL's Emscripten port draws into Module.canvas, the element its default selector "#canvas" names.
const canvas = document.getElementById('canvas');

try {
    // runMain resolves with the value the managed Main returns; it rejects when Main throws, which the default OnException does.
    const exitCode = await dotnet.withModuleConfig({ canvas }).runMain();
    console.log(`Pixely exited with code ${exitCode}`);
    if (exitCode !== 0) {
        showStopped(`Exit code ${exitCode}.`);
    }
} catch (error) {
    console.error('Pixely failed', error);
    showStopped(error?.message ?? String(error));
    throw error;
}

// Once the app has ended the canvas keeps its last frame, and the console is not where a player looks, so the page says what happened
// over the canvas. A reload is the only recovery a page can offer after startup: a lost GPU device took every GPU resource with it.
function showStopped(detail) {
    const overlay = document.createElement('div');
    overlay.style.cssText = 'position: fixed; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 1em; padding: 1em; box-sizing: border-box; background: rgba(0, 0, 0, 0.85); color: #fff; font: 16px system-ui, sans-serif; text-align: center;';

    const headline = document.createElement('p');
    headline.style.cssText = 'margin: 0; font-size: 1.5em;';
    headline.textContent = readDeviceLoss() ? 'The graphics device was lost.' : 'Pixely stopped.';

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
