"""Cut the AI-generated character sheet (art/source/) into the game's art files.
Usage (repo root): python tools/art/slice_sheet.py write   (probe = only print detected boxes)
Crop boxes for this sheet are tuned by hand below; a new sheet needs new boxes.
Steps: remove the white background
(flood fill from the borders only, so whites inside the characters stay), trim, fit to spec size."""
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
IMG = ROOT / "art" / "source"
OUT = ROOT / "client" / "Assets" / "Resources" / "Art"
KEY = (255, 0, 255)


def cut_background(img, thresh=38):
    """Return RGBA with the border-connected near-white background made transparent and edges feathered."""
    if img.mode == "RGBA" and img.getpixel((0, 0))[3] == 0:
        return img, img.getchannel("A")  # already transparent
    rgb = img.convert("RGB")
    w, h = rgb.size
    flood = rgb.copy()
    step = 8
    seeds = [(x, 0) for x in range(0, w, step)] + [(x, h - 1) for x in range(0, w, step)] + \
            [(0, y) for y in range(0, h, step)] + [(w - 1, y) for y in range(0, h, step)]
    for s in seeds:
        r, g, b = flood.getpixel(s)
        if (r, g, b) != KEY and r > 215 and g > 215 and b > 215:
            ImageDraw.floodfill(flood, s, KEY, thresh=thresh)
    # mask: 255 = character, 0 = background
    mask = Image.new("L", (w, h), 255)
    fp, mp = flood.load(), mask.load()
    for y in range(h):
        for x in range(w):
            if fp[x, y] == KEY:
                mp[x, y] = 0
    mask = mask.filter(ImageFilter.MinFilter(3)).filter(ImageFilter.GaussianBlur(0.8))
    out = rgb.convert("RGBA")
    out.putalpha(mask)
    return out, mask


def segments(profile, min_gap, min_len=20):
    segs, start, gap = [], None, 0
    for i, v in enumerate(profile):
        if v:
            if start is None:
                start = i
            gap = 0
        elif start is not None:
            gap += 1
            if gap >= min_gap:
                if i - gap - start + 1 >= min_len:
                    segs.append((start, i - gap + 1))
                start, gap = None, 0
    if start is not None:
        segs.append((start, len(profile)))
    return segs


def components(mask):
    w, h = mask.size
    mp = mask.load()
    rows = [any(mp[x, y] > 128 for x in range(0, w, 2)) for y in range(h)]
    boxes = []
    for (y0, y1) in segments(rows, 12):
        cols = [any(mp[x, y] > 128 for y in range(y0, y1, 2)) for x in range(w)]
        for (x0, x1) in segments(cols, 10):
            # tighten vertically within this column band
            ys = [y for y in range(y0, y1) if any(mp[x, y] > 128 for x in range(x0, x1, 2))]
            boxes.append((x0, ys[0], x1, ys[-1] + 1))
    return boxes


def despeckle(img, cutoff=90):
    """原圖邊緣有半透明的雜點：alpha 太低的直接清掉。"""
    a = img.getchannel("A").point(lambda v: 0 if v < cutoff else v)
    img = img.copy()
    img.putalpha(a)
    return img


def fit(img, size, anchor_bottom):
    """Scale to fit inside size (keeping aspect), centered; bottom-anchored for troops."""
    img = despeckle(img)
    tw, th = size
    # 去掉旁邊角色被切進來的細碎邊角：只保留主體（最大的那塊，以及至少有它兩成大的部分）
    from cc import components as cc_components
    parts = cc_components(img.getchannel("A"), step=2, min_cells=4, pad=2, sizes=True)
    if parts:
        biggest = max(n for _, n in parts)
        keep = [b for b, n in parts if n >= biggest * .2]
        clean = Image.new("RGBA", img.size, (0, 0, 0, 0))
        for b in keep:
            clean.alpha_composite(img.crop(b), (b[0], b[1]))
        img = clean
    bbox = img.getbbox()
    img = img.crop(bbox)
    s = min(tw / img.width, th / img.height) * (0.94 if not anchor_bottom else 0.98)
    img = img.resize((max(1, round(img.width * s)), max(1, round(img.height * s))), Image.LANCZOS)
    canvas = Image.new("RGBA", size, (0, 0, 0, 0))
    x = (tw - img.width) // 2
    y = th - img.height if anchor_bottom else (th - img.height) // 2
    canvas.alpha_composite(img, (x, y))
    return canvas


def circle_icon(img, size=256):
    """Icons come as framed circles on white: cut background, then clean circular alpha."""
    bbox = img.getbbox()
    img = img.crop(bbox)
    side = min(img.size)
    img = img.crop(((img.width - side) // 2, (img.height - side) // 2, (img.width + side) // 2, (img.height + side) // 2))
    img = img.resize((size, size), Image.LANCZOS)
    m = Image.new("L", (size * 4, size * 4), 0)
    ImageDraw.Draw(m).ellipse((2, 2, size * 4 - 3, size * 4 - 3), fill=255)
    m = m.resize((size, size), Image.LANCZOS)
    alpha = Image.composite(img.getchannel("A"), Image.new("L", (size, size), 0), m)
    img.putalpha(alpha)
    return img


def main(mode):
    sheet = Image.open(IMG / "characters_sheet_v1.webp")
    cut, mask = cut_background(sheet)
    from cc import components as cc_components
    boxes = cc_components(mask)
    # 上排（y 中心 < 一半）由左到右，再下排由左到右
    boxes.sort(key=lambda b: (0 if (b[1] + b[3]) / 2 < sheet.height * .5 else 1, b[0]))
    for i, b in enumerate(boxes):
        print(i, b, (b[2] - b[0], b[3] - b[1]))
    if mode != "write":
        return
    # top row: Hold, Vesper, Karst, icon×3; bottom row: Shield squad, Crossbow squad, Rider
    # 霍德與薇絲珀在原圖裡相連：薇絲珀從霍德的盾牌右邊（x≈352）切出
    hv = boxes[0]
    karst = boxes[1]
    icons = [(b[0] + 8, b[1] + 8, b[2] - 8, b[3] - 8) for b in boxes[2:5]]  # 別切到隔壁頭像的框
    # 薇絲珀：避開左邊霍德的盾緣與右邊卡斯特的坐騎；卡斯特：避開右邊霍德頭像
    boxes = [(362, hv[1], 594, hv[3]), (karst[0] + 6, karst[1], 934, karst[3])] + icons + boxes[5:]
    names = ["Vesper_Portrait", "Karst_Portrait", "Hold_Icon", "Vesper_Icon", "Karst_Icon", "Shield", "Crossbow", "Rider"]
    assert len(boxes) == len(names), len(boxes)
    (OUT / "Heroes").mkdir(parents=True, exist_ok=True)
    (OUT / "Troops").mkdir(parents=True, exist_ok=True)
    for b, name in zip(boxes, names):
        part = cut.crop(b)
        if name.endswith("_Portrait"):
            fit(part, (1024, 1536), False).save(OUT / "Heroes" / f"{name}.png")
        elif name.endswith("_Icon"):
            circle_icon(part).save(OUT / "Heroes" / f"{name}.png")
        elif not name.startswith("_"):
            fit(part, (512, 512), True).save(OUT / "Troops" / f"{name}.png")
    # Hold's portrait: the separate, higher-resolution image
    hold, _ = cut_background(Image.open(IMG / "hold_portrait_v1.webp"))
    fit(hold, (1024, 1536), False).save(OUT / "Heroes" / "Hold_Portrait.png")
    for p in sorted(OUT.rglob("*.png")):
        im = Image.open(p)
        print(p.name, im.size, "transparent corners:", im.getpixel((0, 0))[3] == 0)


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "probe")
