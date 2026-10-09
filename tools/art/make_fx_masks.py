"""Build 2.5D effect masks for the painted building sprites.

For every client/Assets/Resources/Art/Buildings/<name>.png writes <name>_fx.png (same size):
  R = teal covenant runes   -> slow breathing glow
  G = warm window light / glowing mushrooms -> candle flicker
  B = cloth (banners, flags, awnings in saturated red or blue) -> wind sway
and <name>_fx.json with normalized rune spots (u, v from bottom-left) for floating motes.

Masks are colour heuristics, not hand-painted; tune thresholds here and re-run.
Usage (repo root): python tools/art/make_fx_masks.py
"""
import colorsys
import json
from pathlib import Path
from PIL import Image, ImageFilter
from cc import components

ROOT = Path(__file__).resolve().parents[2]
DIR = ROOT / "client" / "Assets" / "Resources" / "Art" / "Buildings"


def classify(r, g, b, a):
    if a < 128:
        return 0, 0, 0
    h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
    hd = h * 360
    rune = 1.0 if 160 <= hd <= 190 and s > 0.4 and v > 0.7 else 0.0
    warm = 1.0 if 28 <= hd <= 52 and s > 0.55 and v > 0.9 else 0.0
    cloth = 1.0 if ((hd < 8 or hd > 345) and s > 0.65 and 0.35 < v < 0.9) or (215 <= hd <= 245 and s > 0.55 and 0.3 < v < 0.85) else 0.0
    return rune, warm, cloth


def keep_shapes(mask, rule):
    """Zero every pixel outside connected blobs that pass rule(w, h, cells, fill)."""
    out = Image.new("L", mask.size, 0)
    for (x0, y0, x1, y1), n in components(mask, step=2, min_cells=3, pad=1, sizes=True):
        w, h = x1 - x0, y1 - y0
        fill = n * 4 / max(1, w * h)
        if rule(w, h, n, fill, mask.size):
            out.paste(mask.crop((x0, y0, x1, y1)), (x0, y0))
    return out


def spots(mask, cells=24):
    """Centroids of rune pixels on a coarse grid -> list of (u, v)."""
    w, h = mask.size
    px = mask.load()
    cw, ch = w // cells, h // cells
    out = []
    for gy in range(cells):
        for gx in range(cells):
            n = sx = sy = 0
            for y in range(gy * ch, (gy + 1) * ch, 2):
                for x in range(gx * cw, (gx + 1) * cw, 2):
                    if px[x, y] > 128:
                        n += 1
                        sx += x
                        sy += y
            if n >= 12:
                out.append((round(sx / n / w, 3), round(1 - sy / n / h, 3)))
    return out[:8]


def main():
    for p in sorted(DIR.glob("*.png")):
        if p.stem.endswith("_fx"):
            continue
        im = Image.open(p).convert("RGBA")
        w, h = im.size
        src = im.load()
        r_l, g_l, b_l = (Image.new("L", (w, h), 0) for _ in range(3))
        rp, gp, bp = r_l.load(), g_l.load(), b_l.load()
        for y in range(h):
            for x in range(w):
                ru, wa, cl = classify(*src[x, y])
                rp[x, y], gp[x, y], bp[x, y] = int(ru * 255), int(wa * 255), int(cl * 255)
        # 契印：小而亮的發光塊（排除大片的青綠屋頂）
        r_l = keep_shapes(r_l, lambda w, h, n, fill, sz: w * h < sz[0] * sz[1] * 0.015)
        # 窗燈與發光菇：成塊的亮點（排除細長的金色飾邊）
        g_l = keep_shapes(g_l, lambda w, h, n, fill, sz: fill > 0.35 and min(w, h) >= 6)
        # 布料：細長下垂的旗幟布條（排除大片的屋頂瓦）
        b_l = keep_shapes(b_l, lambda w, h, n, fill, sz: h > w * 1.4 and w * h < sz[0] * sz[1] * 0.02)
        # soften so the glow has a halo and cloth sway has no hard seams
        r_s = r_l.filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(3))
        g_s = g_l.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.GaussianBlur(2))
        b_s = b_l.filter(ImageFilter.GaussianBlur(2))
        Image.merge("RGB", (r_s, g_s, b_s)).save(DIR / f"{p.stem}_fx.png")
        info = {"runes": spots(r_l)}
        (DIR / f"{p.stem}_fx.json").write_text(json.dumps(info))
        cov = [sum(1 for v in ch.get_flattened_data() if v > 128) / (w * h) * 100 for ch in (r_l, g_l, b_l)]
        print(f"{p.stem:15} rune {cov[0]:4.1f}%  warm {cov[1]:4.1f}%  cloth {cov[2]:4.1f}%  spots {len(info['runes'])}")


if __name__ == "__main__":
    main()
