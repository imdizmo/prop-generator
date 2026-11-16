#!/usr/bin/env bash
# Builds a release package: scripts/package.sh <rid: linux-x64> <version, v1.2.3> [out dir]
# The app is published self-contained as one program (no .NET needed to run it); its native libraries (V8, Skia,
# HarfBuzz) stay beside it, since V8 is loaded from disk. Beside it go the floor painter (painter/), a standalone
# Python with numpy for the painter (python/, so the user needs no Python), the Blender add-on (blender/), the
# third-party licences (licenses/) and the docs. Linux x64 gets a .tar.gz (with install-desktop-entry.sh) and an
# AppImage, each holding one folder, PropGenerator-<version>-<rid>/, with PropGenerator at its top.
# Only linux-x64 is built for now; Windows (win-x64) is planned: it adds a row to the table below and a .zip.
set -euo pipefail
rid="${1:?usage: package.sh <rid: linux-x64> <version, v1.2.3> [out dir]}"; version="${2:?version}"; out="${3:-dist}"
root="$(cd "$(dirname "$0")/.." && pwd)"
if ! [[ "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    echo "version must look like v1.2.3 or v1.2.3-beta.1 (got $version)" >&2
    exit 2
fi

# What each platform gets: the standalone CPython (python-build-standalone 20261026, the release Asset Shelf pins) and
# the numpy wheel for it, each with its SHA-256, and where the wheel unpacks.
case "$rid" in
    linux-x64)
        exe=PropGenerator
        v8=ClearScriptV8.linux-x64.so
        py=cpython-3.13.18+20261026-x86_64-unknown-linux-gnu-install_only_stripped.tar.gz
        pysum=df15fe60d3ff55bf9dcf25824b5572756bb8816ff93865a273d3301d7066a8b7
        sitedir=python/lib/python3.13/site-packages
        wheel=numpy-2.5.5-cp313-cp313-manylinux_2_27_x86_64.manylinux_2_28_x86_64.whl
        wheelsum=824025655df929268bfe6b47ed91a00d14209a6f760847cf150be446829e1ce4 ;;
    *) echo "rid must be linux-x64 (got $rid; Windows is planned, not in this version)" >&2; exit 2 ;;
esac

name="PropGenerator-${version}-${rid}"
mkdir -p "$out"; out="$(cd "$out" && pwd)"
stage="$out/$name"
rm -rf "${stage:?}"

dotnet publish "$root/src/PropGenerator.Desktop/PropGenerator.Desktop.csproj" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:DebugType=none -p:Version="${version#v}" -o "$stage"
rm -f "$stage"/*.xml "$stage"/*.pdb
rm -f "$stage/floor_params.json"  # Core's copy for its tests; the app reads painter/propgen_painter/floor_params.json
rm -f "$stage/painter/conftest.py" "$stage/painter/pytest.ini" "$stage/painter/bench.py"  # the painter's dev files
[[ -f "$stage/$exe" && -f "$stage/$v8" ]] || { echo "the publish has no $exe or no $v8 beside it" >&2; exit 1; }

cp "$root/README.md" "$root/LICENSE" "$root/NOTICE.md" "$root/CHANGELOG.md" "$stage/"
cp "$root/scripts/install-desktop-entry.sh" "$root/src/PropGenerator.Desktop/Assets/icon.png" "$stage/"

# The Blender add-on, as the zip Blender installs (Edit > Preferences > Add-ons > Install from Disk).
mkdir -p "$stage/blender"
(cd "$root/blender" && python3 -I -c "
import sys, zipfile, pathlib
with zipfile.ZipFile(sys.argv[1], 'w', zipfile.ZIP_DEFLATED) as z:
    for p in sorted(pathlib.Path('prop_generator_bridge').rglob('*')):
        if p.is_file() and '__pycache__' not in p.parts and p.suffix != '.pyc':
            z.write(p)
" "$stage/blender/prop_generator_bridge.zip")

fetch() { # url sha256 file: into the file, from a cache checked every time
    if ! echo "$2  $3" | sha256sum -c --status >/dev/null 2>&1; then
        curl -fsSL --retry 3 -o "$3.part" "$1"
        echo "$2  $3.part" | sha256sum -c --quiet
        mv "$3.part" "$3"
    fi
}

# A standalone CPython for the painter in python/; the app uses it before any Python on PATH. numpy goes into its
# site-packages from the pinned wheel: a wheel is a zip, and unpacking it there installs it (numpy has no install
# steps). numpy is the only package the painter's worker imports. What the painter never uses is left out: pip,
# Tcl/Tk and IDLE, numpy's own tests, the manual pages.
pycache="${PYTHON_CACHE:-$HOME/.cache/prop-generator/python}"
mkdir -p "$pycache"
fetch "https://github.com/astral-sh/python-build-standalone/releases/download/20261026/${py/+/%2B}" "$pysum" "$pycache/$py"
tar -xzf "$pycache/$py" -C "$stage"  # unpacks as python/
site="$stage/$sitedir"
fetch "https://files.pythonhosted.org/packages/cp313/n/numpy/$wheel" "$wheelsum" "$pycache/$wheel"
python3 -I -c "import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])" "$pycache/$wheel" "$site"
[[ -d "$site/numpy" ]] || { echo "numpy is not in $site" >&2; exit 1; }
find "$site/numpy" -type d -name tests -prune -exec rm -rf {} +
rm -rf "$site"/pip "$site"/pip-*.dist-info "$stage"/python/bin/pip* "$stage"/python/bin/idle* "$stage/python/share/man"
rm -rf "$stage"/python/lib/python3.13/{idlelib,tkinter,turtledemo,ensurepip} "$stage"/python/lib/python3.13/lib-dynload/_tkinter.*
rm -rf "$stage"/python/lib/{tcl,tk,itcl,thread}[0-9]* "$stage"/python/lib/lib{tcl,tk,tcl9tk}[0-9]*
"$stage/python/bin/python3" -I -c "import numpy" || { echo "the bundled Python cannot import numpy" >&2; exit 1; }
find "$stage/python" -name __pycache__ -type d -prune -exec rm -rf {} +  # made by the check above

# Third-party licences of what the package carries: each NuGet package compiled into the app or beside it (Avalonia,
# Skia and HarfBuzz, Silk.NET, SharpGLTF, ClearScript and V8, Newtonsoft.Json, the MCP C# SDK and the rest), picked by
# third_party_licenses.py from the publish's restore, which fails if a shipped package has no licence there; the .NET
# and ASP.NET Core runtimes of the self-contained publish, what Skia and HarfBuzz bundle, CPython and numpy (with the
# libraries its wheel bundles). The last two also stay inside python/.
nuget="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
netver="$(dotnet msbuild "$root/src/PropGenerator.Desktop/PropGenerator.Desktop.csproj" -getProperty:BundledNETCoreAppPackageVersion)"
mkdir -p "$stage/licenses"
python3 -I "$root/scripts/third_party_licenses.py" "$root/src/PropGenerator.Desktop/obj/project.assets.json" "$rid" \
    "$nuget" "$stage/licenses" >/dev/null
install -m 644 "$nuget/microsoft.netcore.app.runtime.$rid/$netver/THIRD-PARTY-NOTICES.TXT" "$stage/licenses/dotnet-THIRD-PARTY-NOTICES.txt"
install -m 644 "$nuget/microsoft.aspnetcore.app.runtime.$rid/$netver/THIRD-PARTY-NOTICES.TXT" "$stage/licenses/aspnetcore-THIRD-PARTY-NOTICES.txt"
install -m 644 "$nuget/skiasharp.nativeassets.linux/3.119.4/THIRD-PARTY-NOTICES.txt" "$stage/licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt"
install -m 644 "$nuget/harfbuzzsharp.nativeassets.linux/8.3.1.3/THIRD-PARTY-NOTICES.txt" "$stage/licenses/HarfBuzzSharp-THIRD-PARTY-NOTICES.txt"
install -m 644 "$stage/python/lib/python3.13/LICENSE.txt" "$stage/licenses/CPython.txt"
install -m 644 "$site"/numpy-*.dist-info/licenses/LICENSE.txt "$stage/licenses/numpy.txt"

cd "$out"
tar --owner=0 --group=0 --numeric-owner -czf "$name.tar.gz" "$name"
echo "$out/$name.tar.gz"

# The AppImage: the same folder, plus the files an AppImage needs at its root. appimagetool and the runtime are pinned
# releases, checked by SHA-256, cached in $APPIMAGE_CACHE.
cache="${APPIMAGE_CACHE:-$HOME/.cache/prop-generator/appimage}"
mkdir -p "$cache"
fetch https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage \
    ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0 "$cache/appimagetool-1.9.1-x86_64.AppImage"
fetch https://github.com/AppImage/type2-runtime/releases/download/20251108/runtime-x86_64 \
    2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d "$cache/runtime-20251108-x86_64"
chmod +x "$cache/appimagetool-1.9.1-x86_64.AppImage"
appdir="$name.AppDir"
rm -rf "${appdir:?}"; cp -a "$name" "$appdir"
rm -f "$appdir/install-desktop-entry.sh" "$appdir/icon.png"  # AppImage launchers add the menu entry themselves
printf '#!/bin/sh\nexec "$(dirname "$(readlink -f "$0")")/PropGenerator" "$@"\n' > "$appdir/AppRun"
chmod +x "$appdir/AppRun"
cp "$root/src/PropGenerator.Desktop/Assets/icon.png" "$appdir/prop-generator.png"
cp "$root/src/PropGenerator.Desktop/Assets/icon.png" "$appdir/.DirIcon"
printf '%s\n' '[Desktop Entry]' 'Type=Application' 'Name=Prop Generator' \
    'Comment=Make game props live: painted floor tiles, rocks, cliffs and nature props, exported for Groundwork' \
    'Exec=PropGenerator' 'Icon=prop-generator' 'Terminal=false' 'Categories=Graphics;3DGraphics;' \
    'StartupWMClass=PropGenerator' > "$appdir/prop-generator.desktop"
rm -f "$name.AppImage"
# appimagetool runs unpacked (no FUSE needed), into a folder under the cache rather than /tmp, removed afterwards.
mkdir -p "$cache/tmp"
TMPDIR="$cache/tmp" ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$cache/appimagetool-1.9.1-x86_64.AppImage" --no-appstream \
    --runtime-file "$cache/runtime-20251108-x86_64" "$appdir" "$name.AppImage" >&2
rm -rf "${appdir:?}" "${cache:?}/tmp"
echo "$out/$name.AppImage"
