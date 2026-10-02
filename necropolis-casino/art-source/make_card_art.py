"""Crops each creature out of the source art into a card-shaped picture.

Every source image shows two variants side by side: the base unit on the left
and its empowered form on the right (shown after Animate Dead).
The figures stand close together, so each one is cut out tightly, its edges
faded into a fill of its own background colour, and the card is vignetted.

Usage: python3 make_card_art.py   (needs Pillow; writes into ../img/)
"""
from pathlib import Path
from statistics import median

from PIL import Image, ImageChops, ImageDraw, ImageFilter

HERE = Path(__file__).parent
OUT = HERE.parent / 'img'
CARD_W, CARD_H = 300, 416  # Same 130:180 shape as the cards

# Figure bounds (left, top, right, bottom) in source pixels
FIGURES = {
    'skeleton':    ('skeleton.png',     (64, 118, 250, 452),  (294, 100, 478, 452)),
    'zombie':      ('zombie.png',       (64, 88, 283, 452),   (284, 82, 474, 452)),
    'wight':       ('wight.webp',       (80, 24, 226, 462),   (280, 24, 448, 476)),
    'vampire':     ('vampire.png',      (96, 108, 264, 480),  (255, 108, 462, 480)),
    'lich':        ('lich.png',         (24, 58, 264, 456),   (256, 24, 490, 456)),
    'dark-knight': ('dark-knight.webp', (25, 205, 505, 790),  (520, 255, 985, 805)),
    'bone-dragon': ('bone-dragon.webp', (30, 200, 492, 828),  (498, 195, 1000, 835)),
}

# Figures stand between the name banner (top 15%) and the card's bottom edge
BOX_TOP, BOX_BOTTOM, BOX_SIDE = 0.15, 0.02, 0.04


def edge_colour(img, band=6):
    """Median colour of the crop's outer band, i.e. the background behind the figure."""
    w, h = img.size
    px = img.load()
    samples = [px[x, y] for y in range(h) for x in range(w)
               if x < band or y < band or x >= w - band or y >= h - band]
    return tuple(int(median(c[i] for c in samples)) for i in range(3))


def vignette(size, strength=0.6):
    """Multiply mask: full brightness in the middle, darker toward the edges."""
    w, h = size
    small = Image.new('L', (w // 4, h // 4), 0)
    ImageDraw.Draw(small).ellipse((-w // 16, -h // 20, w // 4 + w // 16, h // 4 + h // 20), fill=255)
    mask = small.resize(size, Image.LANCZOS).filter(ImageFilter.GaussianBlur(w // 6))
    floor = int(255 * (1 - strength))
    return mask.point(lambda v: floor + v * (255 - floor) // 255).convert('RGB')


def feather_mask(size, frac=0.12):
    """Alpha mask that fades the crop's edges into the background."""
    w, h = size
    fx, fy = max(1, int(w * frac)), max(1, int(h * frac * 0.6))
    mask = Image.new('L', size, 0)
    px = mask.load()
    for y in range(h):
        ay = min(1.0, y / fy, (h - 1 - y) / fy)
        for x in range(w):
            ax = min(1.0, x / fx, (w - 1 - x) / fx)
            px[x, y] = int(255 * max(0.0, min(ax, ay)))
    return mask


def make_card(src, box):
    figure = src.crop(box)

    # Background: the colour behind the figure, so the faded edges blend in
    bg = Image.new('RGB', (CARD_W, CARD_H), edge_colour(figure))

    # Foreground: the figure, fitted into the art box and grounded at the bottom
    box_w = CARD_W * (1 - 2 * BOX_SIDE)
    box_h = CARD_H * (1 - BOX_TOP - BOX_BOTTOM)
    fit = min(box_w / figure.width, box_h / figure.height)
    fg = figure.resize((round(figure.width * fit), round(figure.height * fit)), Image.LANCZOS)
    x = (CARD_W - fg.width) // 2
    y = round(CARD_H * (1 - BOX_BOTTOM)) - fg.height
    bg.paste(fg, (x, y), feather_mask(fg.size))
    return ImageChops.multiply(bg, vignette(bg.size))


for unit, (filename, base_box, empowered_box) in FIGURES.items():
    src = Image.open(HERE / filename).convert('RGB')
    make_card(src, base_box).save(OUT / f'{unit}.webp', quality=82, method=6)
    make_card(src, empowered_box).save(OUT / f'{unit}-empowered.webp', quality=82, method=6)
    print('wrote', unit)
