#!/bin/bash
# Puts Prop Generator in the start menu: a launcher in ~/.local/bin and a desktop entry in ~/.local/share/applications.
# The launcher runs this checkout's Release build (build it first, see README) as the user service prop-generator,
# capped at 6 GB, and says so instead of opening a second window. Run it again after moving the checkout.
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
app="$repo/src/PropGenerator.Desktop/bin/Release/net10.0/PropGenerator"
icon="$repo/src/PropGenerator.Desktop/Assets/icon.png"
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications"

cat > "$HOME/.local/bin/prop-generator" <<LAUNCHER
#!/bin/bash
# Prop Generator (the build in $repo), as the user service prop-generator (6 GB cap).
# Already running: says so instead of opening a second one.
if systemctl --user is-active --quiet prop-generator; then
    notify-send -i "$icon" "Prop Generator" "Prop Generator is already open." 2>/dev/null
    exit 0
fi
if [ ! -x "$app" ]; then
    notify-send -i "$icon" "Prop Generator" "Not built yet: run dotnet build PropGenerator.slnx -c Release in $repo." 2>/dev/null
    exit 1
fi
systemctl --user reset-failed prop-generator 2>/dev/null
exec systemd-run --user --unit=prop-generator -p MemoryMax=6G -p MemorySwapMax=0 \\
    --setenv=DISPLAY --setenv=WAYLAND_DISPLAY --setenv=XDG_RUNTIME_DIR \\
    "$app" "\$@"
LAUNCHER
chmod +x "$HOME/.local/bin/prop-generator"

cat > "$HOME/.local/share/applications/prop-generator.desktop" <<ENTRY
[Desktop Entry]
Type=Application
Name=Prop Generator
GenericName=Game Prop Maker
Comment=Make game props live: painted floor tiles, rocks, cliffs and easy-props' nature props, exported for Groundwork
Exec=$HOME/.local/bin/prop-generator
Icon=$icon
Terminal=false
Categories=Development;
Keywords=prop;props;rock;cliff;floor;tile;easy-props;generator;
StartupWMClass=PropGenerator
ENTRY

update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
command -v kbuildsycoca6 >/dev/null && kbuildsycoca6 >/dev/null 2>&1 # KDE only shows new entries after its menu cache is rebuilt
echo "Installed: $HOME/.local/bin/prop-generator and the start menu entry Prop Generator."
