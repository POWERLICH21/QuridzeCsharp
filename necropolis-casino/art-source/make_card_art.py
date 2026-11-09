"""Step 2: turn the creature cutouts into one matching set of neon cards.

The source art comes in different styles (painted, inked, pixel art), so every
creature is redrawn the same way: a dark figure lit in its level's neon colour,
with a glowing outline, standing in a neon crypt arch over a glowing grid floor.
The empowered form (shown after Animate Dead) gets a green Animate Dead aura,
green arch and drifting sparks; its outline keeps the level colour.

Usage: python3 make_card_art.py   (needs Pillow and numpy; reads cutouts/, writes ../img/)
Then run ../build.py to embed the new pictures into the game pages.
"""
import random
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).parent
CUTOUTS = HERE / 'cutouts'
OUT = HERE.parent / 'img'

# Level colours: keep these the same as UNIT_DATA in the game pages
UNITS = {
    'skeleton': '#cfd8ff',     # Lv 1, ice white
    'zombie': '#4dff88',       # Lv 2, green
    'wight': '#38d6ff',        # Lv 3, sky blue
    'vampire': '#c77dff',      # Lv 4, violet
    'lich': '#ff9a3c',         # Lv 5, orange
    'dark-knight': '#ff4d6d',  # Lv 6, red
    'bone-dragon': '#ffe047',  # Lv 7, gold
}
ANIMATE = '#4dff88'            # Animate Dead green, for empowered cards

W, H = 260, 360                # Saved size: 2x the largest card (130 x 180)
SS = 2                         # Drawn at 2x and scaled down, for smooth lines
FIGURE_TOP = 0.15              # Keeps heads clear of the name banner
FEET = 0.93                    # Figures stand here
HORIZON = 0.74                 # Where the back wall meets the grid floor
INK = np.array([7, 8, 18], dtype=np.float64)


def rgb(hex_colour):
    return np.array([int(hex_colour[i:i + 2], 16) for i in (1, 3, 5)], dtype=np.float64)


def mix(a, b, t):
    return a + (b - a) * t


def glow_layer(draw_fn, size, blur):
    """Draws white lines with draw_fn and returns (sharp core, soft glow) as 0-1 arrays."""
    layer = Image.new('L', size, 0)
    draw_fn(ImageDraw.Draw(layer))
    core = np.asarray(layer, dtype=np.float64) / 255
    soft = np.asarray(layer.filter(ImageFilter.GaussianBlur(blur)), dtype=np.float64) / 255
    return core, soft


def add_light(canvas, amount, colour):
    """Adds coloured light (screen-like, never past white)."""
    light = amount[..., None] * colour[None, None, :]
    return 255 - (255 - canvas) * (1 - np.clip(light / 255, 0, 1))


def fit_figure(cutout, cw, ch):
    box_w, box_h = cw * 0.84, ch * (FEET - FIGURE_TOP)
    fit = min(box_w / cutout.width, box_h / cutout.height)
    size = (max(1, round(cutout.width * fit)), max(1, round(cutout.height * fit)))
    small = cutout.convert('RGBa').resize(size, Image.LANCZOS).convert('RGBA')
    return small, ((cw - size[0]) // 2, round(ch * FEET) - size[1])


def make_card(unit, empowered):
    cw, ch = W * SS, H * SS
    colour = rgb(UNITS[unit])
    accent = rgb(ANIMATE) if empowered else colour
    rng = random.Random(f'{unit}-{empowered}')

    cutout = Image.open(CUTOUTS / f"{unit}{'-empowered' if empowered else ''}.png").convert('RGBA')
    figure, (fx, fy) = fit_figure(cutout, cw, ch)
    fw, fh = figure.size
    cx = fx + fw / 2

    # Backdrop: deep ink, a little lighter toward the floor, with a soft pool of the level colour
    yy, xx = np.mgrid[0:ch, 0:cw].astype(np.float64)
    canvas = np.zeros((ch, cw, 3)) + mix(INK, INK * 1.6, (yy / ch)[..., None])
    pool = np.exp(-(((xx - cx) / (cw * 0.42)) ** 2 + ((yy - ch * 0.55) / (ch * 0.38)) ** 2))
    canvas = add_light(canvas, pool * 0.16, colour)

    horizon = ch * HORIZON
    vanish_y = horizon - ch * 0.22

    # Grid floor in perspective, fading toward the horizon
    def floor(d):
        for k in range(1, 9):
            t = (k / 8) ** 1.8
            y = horizon + (ch - horizon) * t
            d.line((0, y, cw, y), fill=int(70 + 150 * t), width=SS)
        for k in range(-7, 8):
            x_bottom = cx + k * cw * 0.16
            x_top = cx + (x_bottom - cx) * (horizon - vanish_y) / (ch - vanish_y)
            d.line((x_top, horizon, x_bottom, ch), fill=150, width=SS)
    core, soft = glow_layer(floor, (cw, ch), 3 * SS)
    depth = np.clip((yy - horizon) / (ch - horizon), 0, 1)
    canvas = add_light(canvas, (core * 0.75 + soft * 0.55) * (0.3 + 0.7 * depth), colour)

    # Neon crypt arch around the creature, and the horizon line
    half = min(cw / 2 - 8 * SS, fw / 2 + 16 * SS)
    arch_top = max(ch * 0.07, fy - 14 * SS)

    def arch(inset):
        def draw(d):
            x0, x1 = cx - half + inset, cx + half - inset
            r = (x1 - x0) / 2
            top = arch_top + inset
            d.arc((x0, top, x1, top + 2 * r), 180, 360, fill=255, width=2 * SS)
            d.line((x0, top + r, x0, horizon), fill=255, width=2 * SS)
            d.line((x1, top + r, x1, horizon), fill=255, width=2 * SS)
        return draw
    core, soft = glow_layer(arch(0), (cw, ch), 6 * SS)
    canvas = add_light(canvas, soft * 0.9, accent)
    canvas = add_light(canvas, core * 0.75, mix(accent, np.full(3, 255.0), 0.45))
    if empowered:
        core, soft = glow_layer(arch(9 * SS), (cw, ch), 4 * SS)
        canvas = add_light(canvas, soft * 0.5 + core * 0.45, accent)
    core, soft = glow_layer(lambda d: d.line((0, horizon, cw, horizon), fill=255, width=SS), (cw, ch), 5 * SS)
    canvas = add_light(canvas, soft * 0.7 + core * 0.6, colour)

    # Where the figure is (soft edge kept for anti-aliasing)
    alpha_img = Image.new('L', (cw, ch), 0)
    alpha_img.paste(figure.getchannel('A'), (fx, fy))
    alpha = np.asarray(alpha_img, dtype=np.float64) / 255

    # Empowered: a green Animate Dead aura behind the creature
    if empowered:
        aura = np.asarray(alpha_img.filter(ImageFilter.MaxFilter(9)).filter(ImageFilter.GaussianBlur(16 * SS)), dtype=np.float64) / 255
        canvas = add_light(canvas, np.clip(aura * 1.6, 0, 1) * 0.6, accent)

    # Figure: its own shading mapped onto one dark-to-neon ramp, so every creature matches
    src = np.zeros((ch, cw, 3))
    src[fy:fy + fh, fx:fx + fw] = np.asarray(figure, dtype=np.float64)[..., :3]
    lum = src @ np.array([0.299, 0.587, 0.114]) / 255
    inside = alpha > 0.5
    lo, hi = np.percentile(lum[inside], 3), np.percentile(lum[inside], 97)
    lum = np.clip((lum - lo) / max(hi - lo, 1e-3), 0, 1) ** 1.1
    top = 0.62 if empowered else 0.5
    stops = [(0.0, INK), (0.45, colour * 0.36), (0.78, colour * 0.85), (1.0, mix(colour, np.full(3, 255.0), top))]
    shade = np.zeros_like(src)
    for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
        part = np.clip((lum - t0) / (t1 - t0), 0, 1)[..., None]
        band = ((lum >= t0) & (lum <= t1))[..., None]
        shade = np.where(band, c0 + (c1 - c0) * part, shade)

    # Back light: the figure's edge catches the neon
    edge_in = alpha - np.asarray(alpha_img.filter(ImageFilter.MinFilter(7)).filter(ImageFilter.GaussianBlur(2 * SS)), dtype=np.float64) / 255
    shade = add_light(shade, np.clip(edge_in, 0, 1) * 0.55, accent)
    canvas = canvas * (1 - alpha[..., None]) + shade * alpha[..., None]

    # Neon outline around the figure
    ring_img = alpha_img.filter(ImageFilter.MaxFilter(5))
    ring = np.clip(np.asarray(ring_img, dtype=np.float64) / 255 - alpha, 0, 1)
    ring_soft = np.asarray(Image.fromarray((ring * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(5 * SS)), dtype=np.float64) / 255
    canvas = add_light(canvas, np.clip(ring_soft * 1.8, 0, 1) * 0.8, colour)
    canvas = add_light(canvas, ring * 0.9, mix(colour, np.full(3, 255.0), 0.5))

    # Empowered: drifting green sparks
    if empowered:
        def sparks(d):
            for _ in range(22):
                x = rng.uniform(cw * 0.08, cw * 0.92)
                y = rng.uniform(ch * 0.12, horizon)
                r = rng.uniform(1.0, 2.4) * SS
                d.ellipse((x - r, y - r, x + r, y + r), fill=255)
        core, soft = glow_layer(sparks, (cw, ch), 3 * SS)
        canvas = add_light(canvas, soft * 0.9 + core * 0.8, mix(accent, np.full(3, 255.0), 0.3))

    # Darker corners and a dark band at the top, so the name stays readable
    vignette = np.clip(1 - 0.55 * (((xx - cw / 2) / (cw * 0.75)) ** 2 + ((yy - ch / 2) / (ch * 0.75)) ** 2), 0.35, 1)
    canvas *= vignette[..., None]
    canvas *= np.clip(0.55 + yy / (ch * 0.16) * 0.45, 0.55, 1)[..., None]

    art = Image.fromarray(np.clip(canvas, 0, 255).astype(np.uint8))
    return art.resize((W, H), Image.LANCZOS)


def main():
    OUT.mkdir(exist_ok=True)
    for unit in UNITS:
        for empowered in (False, True):
            name = f"{unit}{'-empowered' if empowered else ''}.webp"
            make_card(unit, empowered).save(OUT / name, quality=82, method=6)
            print('wrote', name, (OUT / name).stat().st_size // 1024, 'KB')


if __name__ == '__main__':
    main()
