#!/usr/bin/env bash
# Checks where build.sh clones a tag from, with no network and no real build: a scratch $HOME holding a fake
# reference checkout (tags v0.4.2 only), a fake upstream (v0.4.2 and v0.4.5), and pnpm and node stand-ins that do
# nothing. Run: tools/engine/build.test.sh
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SCRATCH="$HOME/.cache/prop-generator/scratch/build-sh-test-$$"
trap 'rm -rf "$SCRATCH"' EXIT
mkdir -p "$SCRATCH/bin"

fail() {
    echo "FAIL: $*" >&2
    exit 1
}

# A repo with a frontend/ folder and one commit per tag.
repo() {
    local dir="$1"
    shift
    git init --quiet "$dir"
    mkdir -p "$dir/frontend"
    for tag in "$@"; do
        echo "$tag" >"$dir/frontend/version"
        git -C "$dir" add frontend/version
        git -C "$dir" -c user.name=test -c user.email=test@localhost commit --quiet -m "$tag"
        git -C "$dir" tag "$tag"
    done
}

FAKE_HOME="$SCRATCH/home"
repo "$FAKE_HOME/.cache/easy-props" v0.4.2
repo "$SCRATCH/upstream" v0.4.2 v0.4.5
printf '#!/bin/sh\nexit 0\n' >"$SCRATCH/bin/pnpm"
printf '#!/bin/sh\nexit 0\n' >"$SCRATCH/bin/node"
chmod +x "$SCRATCH/bin/pnpm" "$SCRATCH/bin/node"

build() {
    env -u EASY_PROPS_REPO HOME="$FAKE_HOME" PATH="$SCRATCH/bin:$PATH" EASY_PROPS_UPSTREAM="file://$SCRATCH/upstream" \
        "$HERE/build.sh" "$1" >"$SCRATCH/log" 2>&1 || {
        cat "$SCRATCH/log" >&2
        fail "build.sh $1 failed"
    }
}

version_of() { cat "$FAKE_HOME/.cache/prop-generator/easy-props-$1/frontend/version"; }
origin_of() { git -C "$FAKE_HOME/.cache/prop-generator/easy-props-$1" remote get-url origin; }

# A tag the reference checkout has comes from it (no network).
build v0.4.2
[ "$(version_of v0.4.2)" = v0.4.2 ] || fail "v0.4.2: wrong checkout"
[[ "$(origin_of v0.4.2)" == *"$FAKE_HOME/.cache/easy-props" ]] || fail "v0.4.2 was not cloned from the reference checkout: $(origin_of v0.4.2)"

# A newer tag the frozen reference checkout lacks comes from upstream.
build v0.4.5
[ "$(version_of v0.4.5)" = v0.4.5 ] || fail "v0.4.5: wrong checkout"
[ "$(origin_of v0.4.5)" = "file://$SCRATCH/upstream" ] || fail "v0.4.5 was not cloned from upstream: $(origin_of v0.4.5)"

# A second run fetches into the existing clone.
build v0.4.5

echo "build.test.sh: ok"
