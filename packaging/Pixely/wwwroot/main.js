import { dotnet } from './_framework/dotnet.js';

// The page is the screen; SDL's Emscripten port draws into Module.canvas, the element its default selector "#canvas" names.
const canvas = document.getElementById('canvas');

try {
    // runMain resolves with the value the managed Main returns; it rejects when Main throws, which the default OnException does.
    const exitCode = await dotnet.withModuleConfig({ canvas }).runMain();
    console.log(`Pixely exited with code ${exitCode}`);
} catch (error) {
    console.error('Pixely failed', error);
    throw error;
}
