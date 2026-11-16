# Notice

Prop Generator is under the [MIT License](LICENSE).

## Code that comes from Groundwork

Some of Prop Generator comes from Groundwork, the map editor it exports to (MIT License), which is built from
[Valheim World Editor](https://github.com/Shuiei/valheim-world-editor) by Thibault Bene. Groundwork's notice says its
Claude connection, among other parts, is his work.

- `src/PropGenerator.Desktop/ClaudeServer.cs`: the way the server is started, guarded and stopped (`Start`, `Stop`,
  `NoSignals`: the slim ASP.NET Core host on 127.0.0.1, the check of the request's host name, the constant-time token
  check, the stateless MCP transport and the shutdown) derives from Groundwork's `editor/Desktop/ClaudeServer.cs`.
- `.editorconfig`, `global.json` and `tests/testconfig.json` are Groundwork's, unchanged.
- `Directory.Build.props` is Groundwork's, with the project name changed.

Those parts keep this notice:

    MIT License

    Copyright (c) 2026 Groundwork contributors
    Copyright (c) 2026 Thibault Bene

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

## Third-party code

Libraries Prop Generator is built with, each under its own license: [Avalonia](https://github.com/AvaloniaUI/Avalonia)
(MIT) with [SkiaSharp](https://github.com/mono/SkiaSharp) and HarfBuzzSharp (MIT, through Avalonia),
[Silk.NET](https://github.com/dotnet/Silk.NET) (MIT), [SharpGLTF](https://github.com/vpenades/SharpGLTF) (MIT)
for the GLB models, [ClearScript](https://github.com/microsoft/ClearScript) (MIT) with its V8 build and
[Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json) (MIT, Copyright (c) 2007 James Newton-King) for running
easy-props' engine, the [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (Apache License 2.0, older parts MIT)
with ASP.NET Core (MIT) for Claude's connection, and [Roslynator](https://github.com/dotnet/roslynator) (Apache License 2.0)
for the build's code checks. The painter uses [NumPy](https://numpy.org) (BSD 3-Clause); the release packages bundle it
with a standalone [CPython](https://www.python.org) (Python Software Foundation License, built by
[python-build-standalone](https://github.com/astral-sh/python-build-standalone)). [Pillow](https://python-pillow.org)
(MIT-CMU) is used only by the painter's tests and tools and is not shipped.

## In the release packages

A release package carries, beside the app, the full licence texts of what it bundles in binary form, in `licenses/`.
The libraries compiled into `PropGenerator` or shipped beside it, one file per project (scripts/third_party_licenses.py
picks them from the build and the package fails to build if one is missing; the texts that the NuGet packages do not
carry themselves are kept in scripts/licenses/):

- `Avalonia.txt`: Avalonia (MIT, Copyright (c) AvaloniaUI OÜ), all its packages.
- `MicroCom.txt` and `Tmds.DBus.txt`: MicroCom.Runtime and Tmds.DBus.Protocol (MIT), which Avalonia uses.
- `SkiaSharp.txt` and `HarfBuzzSharp.txt`: SkiaSharp and HarfBuzzSharp (MIT).
- `Silk.NET.txt`: Silk.NET (MIT).
- `SharpGLTF.txt`: SharpGLTF (MIT, Copyright (c) 2019 Vicente Penades).
- `ClearScript-V8.txt`: ClearScript (MIT, Copyright (c) Microsoft Corporation) and V8 with the libraries V8 bundles
  (among them ICU, zlib and the others it lists), for `ClearScriptV8.linux-x64.so` and the ICU data inside the app.
- `Newtonsoft.Json.txt`: Newtonsoft.Json (MIT, Copyright (c) 2007 James Newton-King).
- `ModelContextProtocol-csharp-sdk.txt`: the MCP C# SDK (Apache License 2.0, with the MIT licence of its older parts).
- `dotnet-extensions.txt`, `Microsoft.Extensions.DependencyModel.txt` and `Microsoft.DotNet.PlatformAbstractions.txt`:
  Microsoft.Extensions.AI.Abstractions, Microsoft.Extensions.DependencyModel and Microsoft.DotNet.PlatformAbstractions
  (MIT, .NET Foundation).

And the rest:

- `dotnet-THIRD-PARTY-NOTICES.txt` and `aspnetcore-THIRD-PARTY-NOTICES.txt`: the .NET and ASP.NET Core runtimes
  (MIT) inside the self-contained app.
- `SkiaSharp-THIRD-PARTY-NOTICES.txt` and `HarfBuzzSharp-THIRD-PARTY-NOTICES.txt`: Skia, HarfBuzz and what they
  bundle, for `libSkiaSharp.so` and `libHarfBuzzSharp.so`.
- `CPython.txt`: the Python in `python/` (PSF License and the licences of the libraries it bundles).
- `numpy.txt`: NumPy (BSD 3-Clause) and the libraries its wheel bundles (OpenBLAS, gfortran's runtime); the licence
  files of NumPy's bundled sources stay in `python/lib/python3.13/site-packages/numpy-*.dist-info/licenses/`.

## Code ported from other projects

This product includes code ported from third-party software. Each ported file starts with a header line naming
where it comes from, for example:

    // Ported from easy-props <path> (MIT, © 2026 AHS12).

The licences of that software follow.

### easy-props

https://github.com/AHS12/easy-props

src/PropGenerator.Engine/Resources/engine.js is easy-props' own code (the release named in engine-pin.json beside it)
with three.js r186 and the parts of its examples it uses, bundled by tools/engine/build.sh and run as they are. The
shared export and maths code in src/PropGenerator.Nature is ported from easy-props, file by file.
src/PropGenerator.Nature/EngineKits/ holds easy-props' engine kits (frontend/engine-kits at 0.4.2: the Godot wind
materials and guide, and the Unreal setup script and guide), and src/PropGenerator.Engine/Resources/thumbs/ its preset
thumbnails (frontend/public/preset-thumbs), copied byte for byte and shipped as they are, under the licence below.

Licence:

    MIT License

    Copyright (c) 2026 AHS12

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### zod

https://github.com/colinhacks/zod

src/PropGenerator.Engine/Resources/engine.js also bundles zod 4.6.7, which easy-props uses to check its settings.
Licence:

    MIT License

    Copyright (c) 2025 Colin McDonnell

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

### three.js

https://threejs.org

The maths types and mesh operations in src/PropGenerator.Nature/Kernel are ported from three.js r186
(src/math, src/core/BufferGeometry.js, src/core/BufferAttribute.js, examples/jsm/utils/BufferGeometryUtils.js), and
the GLB layout of src/PropGenerator.Nature/Export/GlbWriter.cs from its examples/jsm/exporters/GLTFExporter.js.
Those files start with a header line such as:

    // Ported from three.js r186 src/math/Vector3.js (MIT, © 2010-2026 three.js authors).

Licence:

    The MIT License

    Copyright © 2010-2026 three.js authors

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
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.

### V8

https://v8.dev

src/PropGenerator.Nature/Js/Ieee754.cs is ported from V8's src/base/ieee754.cc and src/builtins/math.tq, and
src/PropGenerator.Nature/Js/JsMath.cs follows V8's Math.pow.

Licence:

    Copyright 2006-2011, the V8 project authors. All rights reserved.
    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions are
    met:

        * Redistributions of source code must retain the above copyright
          notice, this list of conditions and the following disclaimer.
        * Redistributions in binary form must reproduce the above
          copyright notice, this list of conditions and the following
          disclaimer in the documentation and/or other materials provided
          with the distribution.
        * Neither the name of Google Inc. nor the names of its
          contributors may be used to endorse or promote products derived
          from this software without specific prior written permission.

    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

### fdlibm (through FreeBSD msun and V8)

src/PropGenerator.Nature/Js/Ieee754.cs carries fdlibm's algorithms and constants.

Licence:

    Copyright (C) 1993-2004 by Sun Microsystems, Inc. All rights reserved.

    Developed at SunSoft, a Sun Microsystems, Inc. business.
    Permission to use, copy, modify, and distribute this
    software is freely granted, provided that this notice
    is preserved.

### meshoptimizer

https://github.com/zeux/meshoptimizer

src/PropGenerator.Engine/Resources/engine.js carries meshoptimizer's WebAssembly build as three.js r186 ships it
(examples/jsm/libs/meshopt_simplifier.module.js), which its SimplifyModifier runs. Licence:

    MIT License

    Copyright (c) 2016-2026 Arseny Kapoulkine

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

## Icons from lucide

The icons in `src/PropGenerator.Desktop/Look/Icons.cs` are lucide icons (lucide-react 1.65.0), turned into path data by
`tools/lucide_icons.py`. Lucide's licence (ISC) requires this notice, and the icons it lists as derived from Feather
are also under the MIT licence below:

    ISC License
    
    Copyright (c) 2026 Lucide Icons and Contributors
    
    Permission to use, copy, modify, and/or distribute this software for any
    purpose with or without fee is hereby granted, provided that the above
    copyright notice and this permission notice appear in all copies.
    
    THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
    WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
    MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
    ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
    WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
    ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
    OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
    
    ---
    
    The following Lucide icons are derived from the Feather project:
    
    airplay, alert-circle, alert-octagon, alert-triangle, aperture, arrow-down-circle, arrow-down-left, arrow-down-right, arrow-down, arrow-left-circle, arrow-left, arrow-right-circle, arrow-right, arrow-up-circle, arrow-up-left, arrow-up-right, arrow-up, at-sign, calendar, cast, check, chevron-down, chevron-left, chevron-right, chevron-up, chevrons-down, chevrons-left, chevrons-right, chevrons-up, circle, clipboard, clock, code, columns, command, compass, corner-down-left, corner-down-right, corner-left-down, corner-left-up, corner-right-down, corner-right-up, corner-up-left, corner-up-right, crosshair, database, divide-circle, divide-square, dollar-sign, download, external-link, feather, frown, hash, headphones, help-circle, info, italic, key, layout, life-buoy, link-2, link, loader, lock, log-in, log-out, maximize, meh, minimize, minimize-2, minus-circle, minus-square, minus, monitor, moon, more-horizontal, more-vertical, move, music, navigation-2, navigation, octagon, pause-circle, percent, plus-circle, plus-square, plus, power, radio, rss, search, server, share, shopping-bag, sidebar, smartphone, smile, square, table-2, tablet, target, terminal, trash-2, trash, triangle, tv, type, upload, x-circle, x-octagon, x-square, x, zoom-in, zoom-out
    
    The MIT License (MIT) (for the icons listed above)
    
    Copyright (c) 2013-present Cole Bemis
    
    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:
    
    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.
    
    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.

## Space Grotesk

The app's typeface, `src/PropGenerator.Desktop/Assets/Fonts/SpaceGrotesk_*.ttf` (Regular, SemiBold, Bold), is Space
Grotesk by Florian Karsten, Copyright 2020 The Space Grotesk Project Authors
(https://github.com/floriankarsten/space-grotesk), licensed under the SIL Open Font License, Version 1.1
(https://openfontlicense.org); the licence's full text is beside them in `Assets/Fonts/OFL.txt`. The files are the
ones the Jatify launcher ships, unchanged.

## Blender add-on

The add-on in `blender/prop_generator_bridge` (shipped as `blender/prop_generator_bridge.zip`) is under this project's
MIT License. It runs inside Blender and uses Blender's Python API (`bpy`); Blender is under the GNU General Public
License (version 3 or later), so the add-on as it runs in Blender is a combined work under the GPL's terms. The MIT
License is compatible with the GPL.
