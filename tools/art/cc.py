"""Connected components on a downsampled alpha mask, merging overlapping/nearby boxes."""
from collections import deque


def components(mask, step=4, min_cells=60, pad=6, sizes=False):
    w, h = mask.size
    mp = mask.load()
    gw, gh = w // step, h // step
    grid = [[mp[x * step, y * step] > 100 for x in range(gw)] for y in range(gh)]
    seen = [[False] * gw for _ in range(gh)]
    boxes = []
    for y in range(gh):
        for x in range(gw):
            if not grid[y][x] or seen[y][x]:
                continue
            q = deque([(x, y)])
            seen[y][x] = True
            x0 = x1 = x
            y0 = y1 = y
            n = 0
            while q:
                cx, cy = q.popleft()
                n += 1
                x0, x1, y0, y1 = min(x0, cx), max(x1, cx), min(y0, cy), max(y1, cy)
                for dx in (-1, 0, 1):
                    for dy in (-1, 0, 1):
                        nx, ny = cx + dx, cy + dy
                        if 0 <= nx < gw and 0 <= ny < gh and grid[ny][nx] and not seen[ny][nx]:
                            seen[ny][nx] = True
                            q.append((nx, ny))
            if n >= min_cells:
                boxes.append([x0 * step, y0 * step, (x1 + 1) * step, (y1 + 1) * step, n])
    # merge boxes that overlap a lot (e.g. a spear drawn separately from its holder)
    merged = True
    while merged:
        merged = False
        for i in range(len(boxes)):
            for j in range(i + 1, len(boxes)):
                a, b = boxes[i], boxes[j]
                ix = min(a[2], b[2]) - max(a[0], b[0])
                iy = min(a[3], b[3]) - max(a[1], b[1])
                small = min((a[2] - a[0]) * (a[3] - a[1]), (b[2] - b[0]) * (b[3] - b[1]))
                if ix > 0 and iy > 0 and ix * iy > small * .3:
                    boxes[i] = [min(a[0], b[0]), min(a[1], b[1]), max(a[2], b[2]), max(a[3], b[3]), a[4] + b[4]]
                    del boxes[j]
                    merged = True
                    break
            if merged:
                break
    out = [(max(0, b[0] - pad), max(0, b[1] - pad), min(w, b[2] + pad), min(h, b[3] + pad)) for b in boxes]
    return list(zip(out, [b[4] for b in boxes])) if sizes else out
