// Linked into dotnet.native.wasm as a NativeFileReference so the runtime is relinked; nothing calls it.
int browser_content_consumer_native(void)
{
    return 0;
}
