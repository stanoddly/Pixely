# Browser harness

Runs a published browser build in Chromium with WebGPU on, echoes the page's console, screenshots the canvas, presses Escape to quit the app and fails on any page error or a non-zero exit code. It is not part of `dotnet test`: it needs a browser publish, which links XDL with the `wasm-tools` workload (docs/hosting.md, XDL in the browser), and a Chromium with a hardware adapter.

```shell
cd tests/Pixely.Browser.Harness
npm install && npx playwright install chromium
node run-page.js ../../tutorials/Pixely.Tutorials.Browser/bin/Release/net11.0-browser/browser-wasm/publish/wwwroot --screenshot page.png
```

`PIXELY_CHROMIUM` names another Chromium executable than the one Playwright installed. Chromium on Linux exposes the hardware adapter only headed with `--enable-features=Vulkan`; headless gets SwiftShader, which loses the device after the first presented frame. `--headless` is there for the window-only case.

`run-offline.js` checks the offline worker of a publish with `PixelyBrowserOffline` (docs/hosting.md, Offline play): it installs, a reload comes from its cache, and once the server answers 404 for the worker and its assets list, as after a publish without the property, it unregisters itself, loads the page again from the network and deletes its caches. One server on one port serves every step. `--nested` also serves the publish at `/proj/`, inside the scope of the one at `/`, and checks that retiring the worker at `/` keeps the worker and the caches of `/proj/`. `--register` registers the worker from the harness, for a publish that never draws the first frame after which `main.js` registers it.

```shell
node run-offline.js ../../tutorials/Pixely.Tutorials.Browser/bin/Release/net11.0-browser/browser-wasm/publish/wwwroot --nested
```
