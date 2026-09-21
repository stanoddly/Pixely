# Browser harness

Runs a published browser build in Chromium with WebGPU on, echoes the page's console, screenshots the canvas, presses Escape to quit the app and fails on any page error or a non-zero exit code. It is not part of `dotnet test`: it needs the SDL archives with the WebGPU backend and a Chromium with a hardware adapter (docs/hosting.md, WebGPU in the browser).

```shell
cd tests/Pixely.Browser.Harness
npm install && npx playwright install chromium
node run-page.js ../../tutorials/Pixely.Tutorials.Browser/bin/Release/net11.0/browser-wasm/publish/wwwroot --screenshot page.png
```

`PIXELY_CHROMIUM` names another Chromium executable than the one Playwright installed. Chromium on Linux exposes the hardware adapter only headed with `--enable-features=Vulkan`; headless gets SwiftShader, which loses the device after the first presented frame. `--headless` is there for the window-only case.
