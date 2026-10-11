"""Placeholder item icons for the backpack (256x256 RGBA, transparent).

Drawn procedurally with Pillow so every item has a picture now; replace any file in
client/Assets/Resources/Art/Items/ with real art of the same name later (spec: docs/art/ICON_SPEC.md).

    python tools/art/make_item_icons.py
"""
import math
import pathlib
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "client/Assets/Resources/Art/Items"
HEROES = ROOT / "client/Assets/Resources/Art/Heroes"
FONT = "C:/Windows/Fonts/msjhbd.ttc"
S = 1024  # drawn at 4x, saved at 256

INK = (22, 18, 20, 255)
GOLD = (232, 196, 118, 255)
GOLD_D = (150, 112, 52, 255)
WOOD = (118, 74, 42, 255)
WOOD_L = (160, 106, 60, 255)
CYAN = (110, 236, 222, 255)


def canvas():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))


def save(img, name):
    # soft drop shadow so icons read on any slot colour
    a = img.split()[3]
    shadow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    shadow.putalpha(a.point(lambda v: int(v * .55)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(14))
    base = canvas()
    base.alpha_composite(shadow, (10, 18))
    base.alpha_composite(img)
    base.resize((256, 256), Image.LANCZOS).save(OUT / f"{name}.png")
    print(name)


def shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c[:3]) + (c[3] if len(c) > 3 else 255,)


def glow(img, xy, r, color):
    g = canvas()
    ImageDraw.Draw(g).ellipse([xy[0] - r, xy[1] - r, xy[0] + r, xy[1] + r], fill=color)
    img.alpha_composite(g.filter(ImageFilter.GaussianBlur(r * .45)))


# ───── Speedups: hourglass, sand colour + corner emblem per category ─────

SPEED = {
    "General": ((240, 200, 110, 255), "star"),
    "Building": ((226, 140, 70, 255), "hammer"),
    "Research": ((110, 170, 240, 255), "flask"),
    "Magic": ((190, 120, 240, 255), "gem"),
    "Training": ((220, 90, 80, 255), "swords"),
}


def emblem(d, kind, cx, cy, r, color):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=INK)
    d.ellipse([cx - r + 14, cy - r + 14, cx + r - 14, cy + r - 14], fill=shade(color, .55), outline=GOLD, width=10)
    k = r * .55
    if kind == "star":
        pts = []
        for i in range(10):
            a = -math.pi / 2 + i * math.pi / 5
            rr = k if i % 2 == 0 else k * .45
            pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
        d.polygon(pts, fill=GOLD)
    elif kind == "hammer":
        d.rounded_rectangle([cx - k * .15, cy - k * .4, cx + k * .15, cy + k], 8, fill=WOOD_L)
        d.rounded_rectangle([cx - k * .8, cy - k * .9, cx + k * .8, cy - k * .3], 10, fill=(200, 205, 210, 255))
    elif kind == "flask":
        d.rectangle([cx - k * .2, cy - k, cx + k * .2, cy - k * .3], fill=(220, 235, 240, 255))
        d.polygon([(cx - k * .2, cy - k * .35), (cx + k * .2, cy - k * .35), (cx + k * .85, cy + k * .8), (cx - k * .85, cy + k * .8)],
                  fill=(150, 200, 250, 255))
        d.polygon([(cx - k * .55, cy + k * .3), (cx + k * .55, cy + k * .3), (cx + k * .85, cy + k * .8), (cx - k * .85, cy + k * .8)],
                  fill=CYAN)
    elif kind == "gem":
        d.polygon([(cx, cy - k), (cx + k * .75, cy - k * .2), (cx, cy + k), (cx - k * .75, cy - k * .2)], fill=(214, 160, 255, 255))
        d.polygon([(cx, cy - k), (cx + k * .75, cy - k * .2), (cx, cy - k * .05)], fill=(244, 220, 255, 255))
    elif kind == "swords":
        for s in (-1, 1):
            d.line([(cx - s * k * .8, cy - k * .8), (cx + s * k * .7, cy + k * .7)], fill=(225, 230, 235, 255), width=22)
            d.line([(cx + s * k * .35, cy + k * .2), (cx + s * k * .05, cy + k * .55)], fill=GOLD, width=20)


def hourglass(name, sand, kind):
    img = canvas()
    d = ImageDraw.Draw(img)
    cx = 470
    top, bot, w = 170, 860, 230
    glow(img, (cx, 560), 300, shade(sand, 1)[:3] + (70,))
    # glass bulbs
    glass = (200, 236, 240, 150)
    bulb = [(cx - w + 30, top + 60), (cx + w - 30, top + 60), (cx + 26, 515), (cx + w - 30, bot - 60), (cx - w + 30, bot - 60), (cx - 26, 515)]
    d.polygon(bulb, fill=glass, outline=INK, width=14)
    # sand: lower heap + upper remainder + falling thread
    d.polygon([(cx - w + 52, bot - 72), (cx + w - 52, bot - 72), (cx + 70, 680), (cx - 70, 680)], fill=sand)
    d.polygon([(cx - 150, 330), (cx + 150, 330), (cx + 22, 500), (cx - 22, 500)], fill=sand)
    d.line([(cx, 505), (cx, 690)], fill=sand, width=14)
    # glint
    d.line([(cx - w + 70, top + 110), (cx - 70, 440)], fill=(255, 255, 255, 170), width=18)
    # posts and caps
    for x in (cx - w - 10, cx + w + 10):
        d.rounded_rectangle([x - 22, top + 20, x + 22, bot - 20], 12, fill=WOOD, outline=INK, width=10)
    for y in (top, bot):
        d.rounded_rectangle([cx - w - 70, y - 48, cx + w + 70, y + 48], 26, fill=WOOD_L, outline=INK, width=14)
        d.line([(cx - w - 40, y - 12), (cx + w + 40, y - 12)], fill=GOLD, width=12)
    emblem(d, kind, 770, 790, 150, sand)
    save(img, f"Speedup_{name}")


# ───── Hero EXP books ─────

def book(name, cover, size):
    img = canvas()
    d = ImageDraw.Draw(img)
    k = size
    cx, cy = 512, 540
    w, h = 300 * k, 380 * k
    glow(img, (cx, cy), 330 * k, cover[:3] + (80,))
    # pages block (offset)
    d.rounded_rectangle([cx - w + 40, cy - h + 40, cx + w + 40, cy + h + 30], 30, fill=(236, 222, 190, 255), outline=INK, width=14)
    for i in range(4):
        y = cy - h + 90 + i * (2 * h - 120) / 4
        d.line([(cx + w + 6, y), (cx + w + 32, y + 10)], fill=(180, 160, 120, 255), width=6)
    # cover
    d.rounded_rectangle([cx - w, cy - h, cx + w, cy + h], 30, fill=cover, outline=INK, width=16)
    d.rounded_rectangle([cx - w, cy - h, cx - w + 70, cy + h], 24, fill=shade(cover, .7))
    d.rounded_rectangle([cx - w + 100, cy - h + 50, cx + w - 40, cy + h - 50], 20, outline=GOLD, width=12)
    # emblem: abyss eye
    d.ellipse([cx - 110 * k + 30, cy - 70 * k, cx + 110 * k + 30, cy + 70 * k], fill=INK, outline=GOLD, width=12)
    d.ellipse([cx - 45 * k + 30, cy - 45 * k, cx + 45 * k + 30, cy + 45 * k], fill=CYAN)
    glow(img, (cx + 30, cy), 90 * k, (110, 236, 222, 140))
    save(img, name)


# ───── Domain scrolls (印譜) ─────

def scroll(name, glyph, color):
    img = canvas()
    d = ImageDraw.Draw(img)
    cx, cy = 512, 512
    glow(img, (cx, cy), 330, color[:3] + (70,))
    paper = (236, 220, 180, 255)
    d.rounded_rectangle([cx - 300, cy - 230, cx + 300, cy + 230], 20, fill=paper, outline=INK, width=14)
    for y in (cy - 150, cy - 90, cy + 90, cy + 150):
        d.line([(cx - 230, y), (cx - 90, y)], fill=(170, 150, 110, 255), width=10)
        d.line([(cx + 90, y), (cx + 230, y)], fill=(170, 150, 110, 255), width=10)
    for x in (cx - 330, cx + 330):
        d.rounded_rectangle([x - 50, cy - 270, x + 50, cy + 270], 40, fill=WOOD_L, outline=INK, width=14)
        d.rounded_rectangle([x - 30, cy - 320, x + 30, cy - 250], 14, fill=GOLD_D, outline=INK, width=10)
        d.rounded_rectangle([x - 30, cy + 250, x + 30, cy + 320], 14, fill=GOLD_D, outline=INK, width=10)
    # seal with glyph
    d.ellipse([cx - 150, cy - 150, cx + 150, cy + 150], fill=color, outline=INK, width=14)
    d.ellipse([cx - 120, cy - 120, cx + 120, cy + 120], outline=GOLD, width=10)
    f = ImageFont.truetype(FONT, 190)
    d.text((cx, cy + 6), glyph, font=f, fill=(255, 246, 225, 255), anchor="mm", stroke_width=6, stroke_fill=INK)
    save(img, name)


# ───── Hero shards (信物): medallion, hero icon inside ─────

def medallion(name, inner):
    img = canvas()
    d = ImageDraw.Draw(img)
    cx, cy, r = 512, 470, 330
    glow(img, (cx, cy), 380, (232, 196, 118, 90))
    # cord
    d.line([(cx - 160, 60), (cx, cy - r + 10), (cx + 160, 60)], fill=WOOD, width=22)
    hexa = [(cx + r * math.cos(a), cy + r * math.sin(a)) for a in [math.pi / 6 + i * math.pi / 3 for i in range(6)]]
    d.polygon(hexa, fill=GOLD_D, outline=INK, width=16)
    hexi = [(cx + (r - 50) * math.cos(a), cy + (r - 50) * math.sin(a)) for a in [math.pi / 6 + i * math.pi / 3 for i in range(6)]]
    d.polygon(hexi, fill=GOLD)
    ir = r - 80
    if inner is None:
        d.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], fill=(18, 26, 34, 255), outline=INK, width=12)
        for k in (.8, .55, .3):
            d.ellipse([cx - ir * k, cy - ir * k, cx + ir * k, cy + ir * k], outline=(60, 150, 150, 255), width=10)
        d.polygon([(cx, cy - 110), (cx + 70, cy), (cx, cy + 110), (cx - 70, cy)], fill=CYAN)
        glow(img, (cx, cy), 120, (110, 236, 222, 160))
    else:
        face = Image.open(inner).convert("RGBA").resize((2 * ir, 2 * ir), Image.LANCZOS)
        mask = Image.new("L", face.size, 0)
        ImageDraw.Draw(mask).ellipse([0, 0, 2 * ir - 1, 2 * ir - 1], fill=255)
        holder = canvas()
        holder.paste(face, (cx - ir, cy - ir), mask)
        d.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], fill=(18, 26, 34, 255))
        img.alpha_composite(holder)
        d = ImageDraw.Draw(img)
        d.ellipse([cx - ir, cy - ir, cx + ir, cy + ir], outline=INK, width=14)
    # shard crystal hanging below
    d.polygon([(cx, cy + r + 150), (cx + 60, cy + r + 30), (cx, cy + r - 20), (cx - 60, cy + r + 30)], fill=CYAN, outline=INK)
    save(img, name)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, (sand, kind) in SPEED.items():
        hourglass(name, sand, kind)
    book("HeroExpSmall", (70, 140, 90, 255), .78)
    book("HeroExpMedium", (60, 110, 180, 255), .88)
    book("HeroExpLarge", (130, 70, 170, 255), 1.0)
    scroll("DomainScrollPower", "崩", (190, 60, 50, 255))
    scroll("DomainScrollTechnique", "巧", (40, 140, 140, 255))
    scroll("DomainScrollSpeed", "迅", (150, 160, 40, 255))
    scroll("DomainScrollDurability", "磐", (120, 96, 70, 255))
    medallion("HeroShardAny", None)
    for hero in ("Hold", "Vesper", "Karst"):
        medallion(f"HeroShard{hero}", HEROES / f"{hero}_Icon.png")


if __name__ == "__main__":
    main()
