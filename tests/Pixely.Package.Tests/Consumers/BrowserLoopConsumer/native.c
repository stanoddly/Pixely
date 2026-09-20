// Linked into dotnet.native.wasm as a NativeFileReference; the module name is the file name, which DllImport("native") matches.
int pixely_native_frame_limit(void)
{
    return 3;
}
