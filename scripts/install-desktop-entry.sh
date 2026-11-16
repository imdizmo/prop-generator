#!/usr/bin/env bash
# Adds Prop Generator to the app menu for the current user, with its icon, so it can be searched for and pinned.
# Run it from the extracted release folder; run it again after moving the folder. --remove takes the entry out.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
entry="$data/applications/prop-generator.desktop"
icon="$data/icons/hicolor/256x256/apps/prop-generator.png"

if [[ "${1:-}" == "--remove" ]]; then
    rm -f -- "${entry:?}" "${icon:?}"
    echo "Removed Prop Generator from the app menu."
    exit 0
fi

[[ -x "$here/PropGenerator" && -f "$here/icon.png" ]] || { echo "Run this from the Prop Generator folder (PropGenerator and icon.png not found in $here)." >&2; exit 1; }
# ponytail: paths with characters the desktop-entry Exec key would need escaped are refused, not escaped
if [[ "$here" == *[\"\`\$\\%]* || "$here" == *$'\n'* ]]; then
    echo "The folder path contains \" \` \$ \\ % or a line break; move Prop Generator to a plainer path and run this again." >&2
    exit 1
fi

mkdir -p "$(dirname "$entry")" "$(dirname "$icon")"
install -m 644 "$here/icon.png" "$icon"
cat > "$entry" <<EOF
[Desktop Entry]
Type=Application
Name=Prop Generator
Comment=Make game props live: painted floor tiles, rocks, cliffs and nature props, exported for Groundwork
Exec="$here/PropGenerator"
TryExec=$here/PropGenerator
Icon=prop-generator
Terminal=false
Categories=Graphics;3DGraphics;
StartupWMClass=PropGenerator
EOF
command -v update-desktop-database >/dev/null && update-desktop-database -q "$(dirname "$entry")" || true
echo "Added Prop Generator to the app menu ($entry)."
