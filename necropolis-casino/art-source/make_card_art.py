"""Step 2: turn the creature cutouts into one matching set of pixel-art cards.

The source art comes in different styles (painted, inked, pixel art), so every
creature is redrawn the same way: low-resolution pixels, one shared Necropolis
palette, a dark sprite outline, and the same crypt backdrop. The empowered form
(shown after Animate Dead) stands in a green spectral aura.

Usage: python3 make_card_art.py   (needs Pillow and numpy; reads cutouts/, writes ../img/)
"""
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).parent
CUTOUTS = HERE / 'cutouts'
OUT = HERE.parent / 'img'
UNITS = ['skeleton', 'zombie', 'wight', 'vampire', 'lich', 'dark-knight', 'bone-dragon']

ART_W, ART_H = 96, 133   # Pixel grid, same 130:180 shape as the cards
SCALE = 4                # Saved at 4x with hard pixel edges
FLOOR_Y = 98             # Where the back wall meets the floor
FEET_Y = 125             # Figures stand here
FIGURE_TOP = 21          # Keeps heads clear of the name banner

# The shared Necropolis palette, one ramp per material (dark to light)
PALETTE_HEX = [
    # Void
    '0b0a10', '15121c', '1f1a28', '2b2436',
    # Grave stone
    '3a3445', '4d4858', '646070', '7e7b89', '9a98a3', 'b8b6be', 'd8d6da',
    # Bone
    '3f3a2c', '5c5440', '7d7356', 'a0956f', 'c4b98e', 'e3d9b2', 'f6f0d8',
    # Dead flesh
    '8a7474', 'b09c96',
    # Blood
    '2e0f14', '4d1519', '72201f', '9a2f2a', 'c04a3a',
    # Rust and leather
    '2a1d16', '432e1f', '5f422b', '7f5a37', 'a27a4c',
    # Gold
    '5a4318', '8a6a24', 'bb9437', 'e2c063', 'f7e39a',
    # Lich purple
    '251433', '3c1f52', '58307a', '7a48a3', 'a172cf',
    # Spectral green
    '0f2420', '17382f', '225446', '31785f', '4ea27f', '7fcfa6', 'bff0d6',
    # Cold steel
    '2a3346', '3e4c66', '5a6f8f',
    # Grave moss
    '232a1c', '343f27', '4b5735', '67744a',
    # Ghost light
    'a6dcc8', 'e2f7ee',
]
PALETTE = np.array([[int(h[i:i + 2], 16) for i in (0, 2, 4)] for h in PALETTE_HEX], dtype=np.float64)
P = {name: tuple(int(c) for c in PALETTE[i]) for name, i in {
    'void': 0, 'night': 1, 'dusk': 2, 'shade': 3, 'stone1': 4, 'stone2': 5, 'stone3': 6,
    'green0': 40, 'green1': 41, 'green2': 42, 'green3': 43, 'green4': 44, 'green5': 45, 'green6': 46,
}.items()}

# 4x4 ordered-dither thresholds, used for see-through parts like ghostly wings
BAYER = (np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]]) + 0.5) / 16


def to_lab(rgb):
    """sRGB (0-255) to CIE Lab, so colours snap to the palette the way the eye groups them."""
    c = rgb / 255.0
    c = np.where(c > 0.04045, ((c + 0.055) / 1.055) ** 2.4, c / 12.92)
    xyz = c @ np.array([[0.4124, 0.2126, 0.0193], [0.3576, 0.7152, 0.1192], [0.1805, 0.0722, 0.9505]])
    xyz /= np.array([0.9505, 1.0, 1.089])
    f = np.where(xyz > 0.008856, np.cbrt(xyz), 7.787 * xyz + 16 / 116)
    return np.stack([116 * f[..., 1] - 16, 500 * (f[..., 0] - f[..., 1]), 200 * (f[..., 1] - f[..., 2])], axis=-1)


PALETTE_LAB = to_lab(PALETTE)
PALETTE_IMAGE = Image.new('P', (1, 1))
PALETTE_IMAGE.putpalette([int(v) for v in PALETTE.flatten()])


def snap_to_palette(rgb):
    lab = to_lab(rgb.astype(np.float64))
    dist = ((lab[..., None, :] - PALETTE_LAB) ** 2).sum(-1)
    return PALETTE[dist.argmin(-1)].astype(np.uint8)


def grade(rgb):
    """Shared lighting: gentle contrast, muted saturation, cold shadows, bone-warm highlights."""
    c = rgb.astype(np.float64) / 255.0
    lum = c @ np.array([0.299, 0.587, 0.114])
    lo, hi = np.percentile(lum, 2), np.percentile(lum, 98)
    stretched = np.clip((lum - lo) / max(hi - lo, 1e-3), 0, 1) * 0.86 + 0.06
    lum_new = 0.5 * lum + 0.5 * stretched                       # Half-way to a common tonal range
    c = c * (lum_new / np.maximum(lum, 1e-3))[..., None]
    grey = (c @ np.array([0.299, 0.587, 0.114]))[..., None]
    c = grey + (c - grey) * 0.88                                # Same muted saturation everywhere
    shadow = np.clip(1 - grey / 0.45, 0, 1)
    light = np.clip((grey - 0.55) / 0.45, 0, 1)
    c += shadow * np.array([-0.01, -0.01, 0.03]) + light * np.array([0.015, 0.01, -0.01])
    return np.clip(c * 255, 0, 255)


def shrink_figure(cutout):
    """Fits the cutout into the art box at pixel resolution, standing on FEET_Y."""
    box_w, box_h = ART_W - 8, FEET_Y - FIGURE_TOP
    fit = min(box_w / cutout.width, box_h / cutout.height)
    size = (max(1, round(cutout.width * fit)), max(1, round(cutout.height * fit)))
    small = cutout.convert('RGBa').resize(size, Image.LANCZOS).convert('RGBA')
    return small, ((ART_W - size[0]) // 2, FEET_Y - size[1])


def draw_backdrop(empowered, figure_box, seed):
    rng = random.Random(seed)
    img = Image.new('RGB', (ART_W, ART_H), P['night'])
    d = ImageDraw.Draw(img)

    # Back wall: crypt bricks, fading into darkness toward the top
    for row, y in enumerate(range(FLOOR_Y - 6, 8, -7)):
        tone = P['dusk'] if y > 40 else P['night']
        mortar = P['night'] if y > 40 else P['void']
        d.rectangle((0, y - 6, ART_W, y), fill=tone)
        d.line((0, y, ART_W, y), fill=mortar)
        for x in range(-(row % 2) * 6, ART_W, 12):
            d.line((x, y - 6, x, y), fill=mortar)
    d.rectangle((0, 0, ART_W, 8), fill=P['void'])

    # Floor: flagstones lit from above
    d.rectangle((0, FLOOR_Y, ART_W, ART_H), fill=P['shade'])
    d.line((0, FLOOR_Y, ART_W, FLOOR_Y), fill=P['stone2'])
    for y in (FLOOR_Y + 9, FLOOR_Y + 21):
        d.line((0, y, ART_W, y), fill=P['dusk'])
    for x in range(-40, ART_W + 40, 16):
        d.line((ART_W / 2 + (x - ART_W / 2) * 0.55, FLOOR_Y, x, ART_H), fill=P['dusk'])

    left, top, right, bottom = figure_box
    cx = (left + right) / 2
    half = min(ART_W / 2 - 3, (right - left) / 2 + 7)
    arch_top = max(10, top - 8)

    def alcove(inset, colour):
        x0, x1 = cx - half + inset, cx + half - inset
        radius = (x1 - x0) / 2
        d.ellipse((x0, arch_top + inset, x1, arch_top + inset + radius * 2), fill=colour)
        d.rectangle((x0, arch_top + inset + radius, x1, FLOOR_Y), fill=colour)

    # Every creature stands in a crypt alcove: stone rim, lit interior
    alcove(0, P['stone1'])
    if empowered:
        # Animate Dead: the alcove fills with spectral green light and drifting motes
        alcove(2, P['green1'])
        alcove(6, P['green2'])
        alcove(11, P['green3'])
        for _ in range(16):
            x, y = rng.randrange(4, ART_W - 4), rng.randrange(12, FEET_Y)
            d.point((x, y), fill=P['green5'] if rng.random() < 0.6 else P['green6'])
    else:
        # Cold moonlight, enough to show dark silhouettes
        alcove(2, P['shade'])
        alcove(6, P['stone1'])
        alcove(11, P['stone2'])
    d.line((cx - half, FLOOR_Y, cx + half, FLOOR_Y), fill=P['stone3'])

    # Shadow pooled under the creature
    sw = max(10, (right - left) * 0.42)
    cx = (left + right) / 2
    d.ellipse((cx - sw, FEET_Y - 3, cx + sw, FEET_Y + 3), fill=P['void'])
    return img


def make_card(unit, empowered):
    cutout = Image.open(CUTOUTS / f"{unit}{'-empowered' if empowered else ''}.png").convert('RGBA')
    small, (fx, fy) = shrink_figure(cutout)
    rgba = np.array(small).astype(np.float64)

    # Hard pixel edges; partly see-through parts (ghost wings, smoke) become dithered
    alpha = rgba[..., 3] / 255.0
    h, w = alpha.shape
    threshold = np.tile(BAYER, (h // 4 + 1, w // 4 + 1))[:h, :w]
    solid = alpha > np.maximum(threshold, 0.18)
    colours = snap_to_palette(grade(rgba[..., :3]))

    canvas = np.array(draw_backdrop(empowered, (fx, fy, fx + w, fy + h), seed=unit))
    region = canvas[fy:fy + h, fx:fx + w]
    region[solid] = colours[solid]

    # One-pixel sprite outline around the figure
    mask = np.zeros((ART_H, ART_W), dtype=bool)
    mask[fy:fy + h, fx:fx + w] = solid
    ring = np.zeros_like(mask)
    ring[1:, :] |= mask[:-1, :]
    ring[:-1, :] |= mask[1:, :]
    ring[:, 1:] |= mask[:, :-1]
    ring[:, :-1] |= mask[:, 1:]
    canvas[ring & ~mask] = P['void']

    art = Image.fromarray(canvas).resize((ART_W * SCALE, ART_H * SCALE), Image.NEAREST)
    return art.quantize(palette=PALETTE_IMAGE, dither=Image.Dither.NONE)  # Exact palette, small PNG


def main():
    OUT.mkdir(exist_ok=True)
    for unit in UNITS:
        for empowered in (False, True):
            name = f"{unit}{'-empowered' if empowered else ''}.png"
            make_card(unit, empowered).save(OUT / name, optimize=True)
            print('wrote', name)


if __name__ == '__main__':
    main()
