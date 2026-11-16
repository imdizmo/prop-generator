#!/usr/bin/env bash
# Builds easy-props' own code into one script the app runs in process (ClearScript V8).
#
#   tools/engine/build.sh v0.4.2          clone or fetch that tag, install, bundle, copy the thumbnails, write the pin
#   tools/engine/build.sh v0.4.2 --smoke  the same, then run the smoke script (every preset, three levels, two exports)
#
# Output, committed with the app: src/PropGenerator.Engine/Resources/{engine.js, shims.js, engine-pin.json, thumbs/}.
#
# The easy-props checkout used is our own clone, ~/.cache/prop-generator/easy-props-<tag>. An optional local reference
# checkout ($EASY_PROPS_DIR, default ~/.cache/easy-props) is only ever read (cloned from), never built in. Their sources are never edited:
# everything we add goes in through tools/engine/entry.ts and shims.js.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
OUT="$REPO/src/PropGenerator.Engine/Resources"
REF="${EASY_PROPS_DIR:-$HOME/.cache/easy-props}"
UPSTREAM="${EASY_PROPS_UPSTREAM:-https://github.com/AHS12/easy-props}"

die() {
    echo "build.sh: $*" >&2
    exit 1
}

[ $# -ge 1 ] || die "usage: build.sh <tag> [--smoke]"
TAG="$1"
SMOKE=""
[ "${2:-}" = "--smoke" ] && SMOKE=1
[[ "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "the tag looks like v0.4.2, got: $TAG"
command -v git >/dev/null || die "git is needed"
command -v pnpm >/dev/null || die "pnpm is needed"
command -v node >/dev/null || die "node is needed"

EP="$HOME/.cache/prop-generator/easy-props-$TAG"

# Where to clone from: EASY_PROPS_REPO when set; else the local reference checkout when it has the tag (no network);
# else upstream. The reference checkout is frozen at an older release, so newer tags come from upstream.
if [ -n "${EASY_PROPS_REPO:-}" ]; then
    SOURCE="$EASY_PROPS_REPO"
elif [ -d "$REF/.git" ] && git -C "$REF" rev-parse --quiet --verify "refs/tags/$TAG^{commit}" >/dev/null; then
    SOURCE="$REF"
else
    SOURCE="$UPSTREAM"
fi

# 1. Our clone at the tag.
if [ ! -d "$EP/.git" ]; then
    echo "== cloning $TAG from $SOURCE into $EP"
    FROM="$SOURCE"
    [ -d "$SOURCE" ] && FROM="file://$SOURCE"
    git -c advice.detachedHead=false clone --quiet --depth 1 --branch "$TAG" "$FROM" "$EP"
else
    echo "== fetching $TAG into $EP"
    git -C "$EP" fetch --quiet --depth 1 origin "refs/tags/$TAG:refs/tags/$TAG"
    git -C "$EP" checkout --quiet --detach "refs/tags/$TAG"
fi
COMMIT="$(git -C "$EP" rev-parse "refs/tags/$TAG^{commit}")"
[ "$(git -C "$EP" rev-parse HEAD)" = "$COMMIT" ] || die "$EP is not at $TAG"
[ -z "$(git -C "$EP" status --porcelain --untracked-files=no)" ] || die "$EP has local changes; this script never edits their sources"

# 2. Their dependencies, exactly as locked (esbuild comes with them, through vite).
echo "== pnpm install --frozen-lockfile"
(cd "$EP/frontend" && pnpm install --frozen-lockfile --prefer-offline --reporter=silent)

# 3. The bundle, their preset pictures, the shims beside it and the pin (build.mjs does all of it).
echo "== bundling"
node "$HERE/build.mjs" --easy-props "$EP" --tag "$TAG" --commit "$COMMIT" --out "$OUT"

if [ -n "$SMOKE" ]; then
    echo "== smoke"
    node "$HERE/smoke.mjs" --engine "$OUT/engine.js" --shims "$OUT/shims.js" --easy-props "$EP"
fi
