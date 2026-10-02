# Third-Party Notices

Pixely depends on the build-time [`SlangDxcBundle.Toolchain`](https://www.nuget.org/packages/SlangDxcBundle.Toolchain) package produced by [`stanoddly/slang-dxc-bundle`](https://github.com/stanoddly/slang-dxc-bundle). Its binaries and third-party components are distributed under their respective licenses, not under Pixely's MIT license.

The complete license and notice files supplied with each distribution are retained in that package under:

```text
tools/slang/<platform>/LICENSE
tools/slang/<platform>/LICENSES/
```

This includes the Slang license, identified by the distribution as `Apache-2.0 WITH LLVM-exception`, the DirectX Shader Compiler license, and the third-party notices and license texts applicable to their bundled components.

## Brotli decoder

The package ships `wwwroot/brotli-decode.js`, which a browser publish copies into its `wwwroot/`. It is `js/decode.min.js` from [`google/brotli`](https://github.com/google/brotli) v1.2.0, distributed under the MIT license:

```text
Copyright (c) 2009, 2010, 2013-2016 by the Brotli Authors.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
```
