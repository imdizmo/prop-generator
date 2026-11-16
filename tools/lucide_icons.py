#!/usr/bin/env python3
"""Prints the lucide icons the app uses as the C# table pasted into src/PropGenerator.Desktop/Look/Icons.cs.

The icons come from easy-props' installed lucide-react (v1.65.0, ISC licence; its notice is in NOTICE.md).
Each icons/<name>.mjs holds an `__iconData` object whose `node` lists [tag, attributes] shapes on a 24x24 grid.
The files are only read as text, never run. Usage (from the repo root):

    python3 -I tools/lucide_icons.py <path to lucide-react/dist/esm/icons> > scratch.txt

Every shape becomes SVG path data (circles, rects, lines, polylines and ellipses are turned into path commands), and
the shapes of one icon are joined into a single string. The icons are drawn stroked (width 2, round caps and joins).
"""
import os
import re
import sys

NAMES = [
    "trees", "search", "save", "download", "check", "chevron-right", "chevron-down", "chevron-left", "x", "dices",
    "layout-grid", "rotate-ccw", "triangle-alert", "mouse-pointer-2", "move", "rotate-3d", "sun", "box", "grid-2x2",
    "wind", "layers", "tree-pine", "camera", "frame", "info", "person-standing", "brick-wall", "maximize", "minimize",
    "sliders-horizontal", "shapes", "settings-2", "copy", "trash-2", "keyboard", "undo-2", "redo-2", "folder-open",
    "file-down", "settings", "clipboard-copy", "house", "hash", "panel-left-close", "layout-panel-left", "send", "folder",
    "grid-3x3", "arrow-left", "sprout", "zap", "axe", "triangle-right",
]

SHAPE = re.compile(r'\[\s*"(path|circle|rect|line|polyline|ellipse)"\s*,\s*\{(.*?)\}\s*\]', re.S)
ATTR = re.compile(r'(\w+)\s*:\s*(?:"([^"]*)"|([-\d.]+))')


def num(v):
    """Formats a number without a trailing .0."""
    return ("%g" % float(v))


def ellipse(cx, cy, rx, ry):
    """Path data for an ellipse as two arcs."""
    return (f"M{num(cx - rx)} {num(cy)}a{num(rx)} {num(ry)} 0 1 0 {num(2 * rx)} 0"
            f"a{num(rx)} {num(ry)} 0 1 0 {num(-2 * rx)} 0Z")


def shape_to_path(tag, a):
    """Path data for one lucide shape."""
    f = lambda k, d="0": float(a.get(k, d))
    if tag == "path":
        return a["d"]
    if tag == "circle":
        return ellipse(f("cx"), f("cy"), f("r"), f("r"))
    if tag == "ellipse":
        return ellipse(f("cx"), f("cy"), f("rx"), f("ry"))
    if tag == "line":
        return f"M{num(f('x1'))} {num(f('y1'))}L{num(f('x2'))} {num(f('y2'))}"
    if tag == "polyline":
        pts = a["points"].replace(",", " ").split()
        pairs = [f"{pts[i]} {pts[i + 1]}" for i in range(0, len(pts), 2)]
        return "M" + "L".join(pairs)
    if tag == "rect":
        x, y, w, h = f("x"), f("y"), f("width"), f("height")
        rx = f("rx", a.get("ry", "0"))
        ry = f("ry", a.get("rx", "0"))
        rx, ry = min(rx, w / 2), min(ry, h / 2)
        if rx == 0 and ry == 0:
            return f"M{num(x)} {num(y)}h{num(w)}v{num(h)}h{num(-w)}Z"
        return (f"M{num(x + rx)} {num(y)}h{num(w - 2 * rx)}a{num(rx)} {num(ry)} 0 0 1 {num(rx)} {num(ry)}"
                f"v{num(h - 2 * ry)}a{num(rx)} {num(ry)} 0 0 1 {num(-rx)} {num(ry)}"
                f"h{num(-(w - 2 * rx))}a{num(rx)} {num(ry)} 0 0 1 {num(-rx)} {num(-ry)}"
                f"v{num(-(h - 2 * ry))}a{num(rx)} {num(ry)} 0 0 1 {num(rx)} {num(-ry)}Z")
    raise ValueError(tag)


ALIAS = re.compile(r"export \{ default \} from '\./([\w-]+)\.mjs'")


def read_icon(folder, name):
    """The text of an icon's data, following a file that only re-exports another icon (such as trash-2 -> trash)."""
    for _ in range(5):
        with open(os.path.join(folder, name + ".mjs"), encoding="utf-8") as fh:
            text = fh.read()
        alias = ALIAS.search(text)
        if alias is None:
            return text[text.index("__iconData"):]
        name = alias.group(1)
    raise SystemExit("alias loop at " + name)


def main():
    """Prints one `["name"] = "path data",` line per icon."""
    folder = sys.argv[1]
    for name in NAMES:
        text = read_icon(folder, name)
        parts = []
        for tag, body in SHAPE.findall(text):
            attrs = {k: (s or n) for k, s, n in ATTR.findall(body)}
            parts.append(shape_to_path(tag, attrs))
        if not parts:
            raise SystemExit("no shapes found in " + name)
        print(f'        ["{name}"] = "{"|".join(parts)}",')


main()
