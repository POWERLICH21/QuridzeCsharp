"""Step 1: cut every creature out of its source image (transparent background).

Each source image shows two variants side by side: the weaker base unit on the
left and its stronger form on the right (shown only after Animate Dead).
Results go to cutouts/<unit>.png and cutouts/<unit>-empowered.png; they can be
touched up by hand before running make_card_art.py.

Usage: pip install "rembg[cpu]" && python3 cut_figures.py
"""
from pathlib import Path

import numpy as np
from PIL import Image
from rembg import new_session, remove

HERE = Path(__file__).parent
OUT = HERE / 'cutouts'

# Figure bounds (left, top, right, bottom) in source pixels: base, empowered
FIGURES = {
    'skeleton':    ('skeleton.png',     (64, 118, 250, 452),  (294, 100, 478, 452)),
    'zombie':      ('zombie.png',       (64, 88, 283, 452),   (284, 82, 474, 452)),
    'wight':       ('wight.webp',       (80, 24, 226, 462),   (280, 24, 448, 476)),
    'vampire':     ('vampire.png',      (96, 108, 264, 480),  (255, 108, 462, 480)),
    'lich':        ('lich.png',         (24, 58, 264, 456),   (256, 24, 490, 456)),
    'dark-knight': ('dark-knight.webp', (25, 205, 505, 790),  (520, 255, 985, 805)),
    'bone-dragon': ('bone-dragon.webp', (30, 200, 492, 828),  (498, 195, 1000, 835)),
}


def keep_main_shapes(alpha, min_share=0.04):
    """Drops specks and slivers of the neighbouring figure; keeps the creature and its large props."""
    solid = alpha > 96
    h, w = solid.shape
    labels = np.zeros((h, w), dtype=np.int32)
    sizes = [0]
    for y0, x0 in zip(*np.nonzero(solid)):
        if labels[y0, x0]:
            continue
        label = len(sizes)
        stack, count = [(y0, x0)], 0
        labels[y0, x0] = label
        while stack:
            y, x = stack.pop()
            count += 1
            for ny, nx in ((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)):
                if 0 <= ny < h and 0 <= nx < w and solid[ny, nx] and not labels[ny, nx]:
                    labels[ny, nx] = label
                    stack.append((ny, nx))
        sizes.append(count)
    if len(sizes) == 1:
        return alpha
    biggest = max(sizes)
    keep = np.array([False] + [s >= biggest * min_share for s in sizes[1:]])
    # Soft edge pixels follow the shape they border
    grown = keep[labels]
    for _ in range(2):
        grown = grown | np.roll(grown, 1, 0) | np.roll(grown, -1, 0) | np.roll(grown, 1, 1) | np.roll(grown, -1, 1)
    return np.where(grown, alpha, 0).astype(np.uint8)


def main():
    OUT.mkdir(exist_ok=True)
    session = new_session('birefnet-general-lite')
    for unit, (filename, base_box, empowered_box) in FIGURES.items():
        src = Image.open(HERE / filename).convert('RGB')
        for suffix, box in (('', base_box), ('-empowered', empowered_box)):
            cut = remove(src.crop(box), session=session).convert('RGBA')
            rgba = np.array(cut)
            rgba[..., 3] = keep_main_shapes(rgba[..., 3])
            Image.fromarray(rgba).save(OUT / f'{unit}{suffix}.png', optimize=True)
            print('cut', unit + suffix)


if __name__ == '__main__':
    main()
