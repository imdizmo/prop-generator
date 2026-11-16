#!/usr/bin/env python3
"""The licences of the NuGet packages the app ships, for a release package's licenses/.

    third_party_licenses.py <project.assets.json> <rid> <nuget packages dir> [out dir]

Reads which packages put code into the published app for <rid> (their runtime or native assets) from the Desktop
project's restore output, and gives each its licence: the file inside the package where it carries one, else the
upstream text in scripts/licenses/ (for packages that only name their licence). With an out dir it copies them there;
without one it only checks. Either way it fails, naming them, when a shipped package has no entry below, so a new
dependency cannot reach a release without its licence. CI runs the check on every push.
"""
import fnmatch
import json
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))

# Package id (glob, first match wins) -> (file in licenses/, source). Source "nuget:<path>" is a file inside that
# package in the NuGet cache; "repo:<name>" is scripts/licenses/<name>; "covered:<file>" means another entry's file (or
# one package.sh copies itself) already holds its licence.
TABLE = [
    ("Avalonia", "Avalonia.txt", "repo:Avalonia.txt"),
    ("Avalonia.*", "Avalonia.txt", "repo:Avalonia.txt"),
    ("MicroCom.Runtime", "MicroCom.txt", "repo:MicroCom.txt"),
    ("Tmds.DBus.Protocol", "Tmds.DBus.txt", "repo:Tmds.DBus.txt"),
    ("SkiaSharp", "SkiaSharp.txt", "nuget:LICENSE.txt"),
    ("SkiaSharp.NativeAssets.*", None, "covered:SkiaSharp-THIRD-PARTY-NOTICES.txt"),
    ("HarfBuzzSharp", "HarfBuzzSharp.txt", "nuget:LICENSE.txt"),
    ("HarfBuzzSharp.NativeAssets.*", None, "covered:HarfBuzzSharp-THIRD-PARTY-NOTICES.txt"),
    ("Silk.NET.*", "Silk.NET.txt", "repo:Silk.NET.txt"),
    ("SharpGLTF.*", "SharpGLTF.txt", "repo:SharpGLTF.txt"),
    ("Microsoft.ClearScript.*", "ClearScript-V8.txt", "nuget:License.txt"),
    ("Newtonsoft.Json", "Newtonsoft.Json.txt", "nuget:LICENSE.md"),
    ("ModelContextProtocol", "ModelContextProtocol-csharp-sdk.txt", "repo:ModelContextProtocol-csharp-sdk.txt"),
    ("ModelContextProtocol.*", "ModelContextProtocol-csharp-sdk.txt", "repo:ModelContextProtocol-csharp-sdk.txt"),
    ("Microsoft.Extensions.AI.Abstractions", "dotnet-extensions.txt", "repo:dotnet-extensions.txt"),
    ("Microsoft.Extensions.DependencyModel", "Microsoft.Extensions.DependencyModel.txt", "nuget:LICENSE.TXT"),
    ("Microsoft.DotNet.PlatformAbstractions", "Microsoft.DotNet.PlatformAbstractions.txt", "nuget:LICENSE.TXT"),
]


def shipped(assets_path, rid):
    """(id, version) of each package with runtime or native assets for rid."""
    with open(assets_path, encoding="utf-8") as f:
        assets = json.load(f)
    targets = assets["targets"]
    with_rid = [k for k in targets if k.endswith("/" + rid)]
    if with_rid:  # a restore for the rid (dotnet publish -r): its assets are the rid's already
        libs, prefix = targets[with_rid[0]], None
    else:  # a plain restore: the rid's natives are among runtimeTargets
        libs, prefix = targets[next(k for k in targets if "/" not in k)], "runtimes/%s/" % rid
    out = []
    for key, lib in sorted(libs.items()):
        if lib.get("type") != "package":
            continue
        files = [p for p in list(lib.get("runtime", {})) + list(lib.get("native", {})) if not p.endswith("/_._")]
        if prefix:
            files += [p for p in lib.get("runtimeTargets", {}) if p.startswith(prefix) and not p.endswith("/_._")]
        if files:
            name, version = key.split("/", 1)
            out.append((name, version))
    return out


def main(argv):
    if len(argv) not in (4, 5):
        sys.exit(__doc__)
    assets_path, rid, nuget = argv[1:4]
    dest = argv[4] if len(argv) == 5 else None
    missing, copies = [], {}
    for name, version in shipped(assets_path, rid):
        entry = next((e for e in TABLE if fnmatch.fnmatchcase(name, e[0])), None)
        if entry is None:
            missing.append("%s %s (no entry)" % (name, version))
            continue
        _, out_name, source = entry
        kind, _, rel = source.partition(":")
        if kind == "covered":
            continue
        src = os.path.join(nuget, name.lower(), version, rel) if kind == "nuget" else os.path.join(HERE, "licenses", rel)
        if not os.path.isfile(src):
            missing.append("%s %s (no %s)" % (name, version, src))
            continue
        copies.setdefault(out_name, src)
    if missing:
        sys.exit("shipped packages without a licence in scripts/third_party_licenses.py:\n  " + "\n  ".join(missing))
    if dest:
        os.makedirs(dest, exist_ok=True)
        for out_name, src in sorted(copies.items()):
            shutil.copyfile(src, os.path.join(dest, out_name))
            os.chmod(os.path.join(dest, out_name), 0o644)
    for out_name in sorted(copies):
        print(out_name)


if __name__ == "__main__":
    main(sys.argv)
