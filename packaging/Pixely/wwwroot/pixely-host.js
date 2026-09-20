// Owns the requestAnimationFrame loop for Pixely.App.BrowserHost. The managed callback is marshalled once, here, not per frame,
// and its proxy is disposed when the loop ends so the delegate and the app it targets stop being rooted by a GC handle.
// Everything sits inside the try so the promise rejects instead of the error stopping at the browser console: an exception thrown
// inside a requestAnimationFrame callback never reaches the awaiting managed Main by itself.
export function runFrameLoop(runFrame) {
    return new Promise((resolve, reject) => {
        const finish = (settle, value) => {
            runFrame.dispose?.();
            settle(value);
        };
        const tick = () => {
            try {
                if (runFrame()) {
                    globalThis.requestAnimationFrame(tick);
                } else {
                    finish(resolve);
                }
            } catch (error) {
                finish(reject, error);
            }
        };
        try {
            globalThis.requestAnimationFrame(tick);
        } catch (error) {
            finish(reject, error);
        }
    });
}
