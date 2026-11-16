"""Makes the floor's picture for the objects drawer: src/PropGenerator.Desktop/Assets/floor_v5.png.

Source: the hand-painted v5 floor atlas (1024 x 1024), the one the painter's golden test keeps as
painter/tests/golden/floor_atlas_v5.png. Its top area (rows 224 to 1024)
holds the plank faces; the end-grain strip starts about row 800. This takes the centre square of the plank faces and
shrinks it to 128 x 128. Run once and commit the PNG:

    python3 -I tools/make_floor_thumb.py <atlas.png> <out.png>
"""
import sys

from PIL import Image

TOP = 224       # first row of the plank area
BOTTOM = 800    # plank faces end here (end grain below)
SIZE = 128


def main(atlas_path: str, out_path: str) -> None:
    atlas = Image.open(atlas_path).convert("RGB")
    side = BOTTOM - TOP
    left = (atlas.width - side) // 2
    square = atlas.crop((left, TOP, left + side, TOP + side))
    square.resize((SIZE, SIZE), Image.LANCZOS).save(out_path, optimize=True)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("usage: make_floor_thumb.py <atlas.png> <out.png>")
    main(sys.argv[1], sys.argv[2])
