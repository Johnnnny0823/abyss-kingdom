"""City-view buildings in the same Covenant Ruins style as generate_world.py.

Art direction (2026-10-09): sedimented-myth ruins. Stone walls carry horizontal strata bands,
buildings sit on stepped terraces, higher tiers add colonnades and ziggurat-like steps,
moss creeps over the masonry. Chunky, big-roofed, readable-from-afar proportions typical of
mobile 4X games. All shapes are original; no designs from any specific work are copied.

blender --background --python tools/blender/generate_city.py
One model per building and look tier: <Building>_1 (Lv.1-3), _2 (Lv.4-6), _3 (Lv.7-10),
plus Site (not built yet). 1 meter = 1 city grid cell; Keep is 4x4 cells, others 3x3.
Reuses the shape helpers, palette and exporter from generate_world.py.
"""
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate_world as gw  # noqa: E402
from generate_world import box, cone, rock, beam, rune  # noqa: E402

gw.OUTPUT = gw.ROOT / 'client/Assets/Resources/Models/City'
gw.MAX_MATERIALS = 8  # buildings are few and large; a richer palette reads better
gw.REPORTS = gw.ROOT / 'artifacts/models/city'
gw.PALETTE.update({'roof': '#5B80A8', 'plaster': '#D8CFBD', 'herb': '#66B060', 'moss': '#5E8F55',
                   'roofwarm': '#B4654A', 'white': '#F2ECDF', 'ochre': '#C9A15E', 'crimson': '#A8414A',
                   'timber': '#9A6A43', 'leafroof': '#4F9A55'})
# Each building has its own identity color so it reads from afar:
# Keep royal blue, Sawmill timber, Quarry ochre stone, Farm green roof, Crystal mine teal,
# Warehouse warm red roof, Barracks crimson, Hospital white with a teal roof.

TRIS = 2000


def pad(w, color='dark'):
    # Stepped terrace: two stacked plinths, like a worn temple base.
    cone('Foundation', (0, 0, .05), w * .56, .1, color, 8, w * .54, math.pi / 8)
    cone('Terrace', (0, 0, .13), w * .5, .07, 'stone', 8, w * .49, math.pi / 8)


def strata(x, y, w, d, h, bands=2):
    # Thin darker courses across a wall: the sediment layers of the world.
    for i in range(bands):
        box('Strata', (x, y, .18 + h * (i + 1) / (bands + 1)), (w * 1.02, d * 1.02, .05), 'dark')


def moss(x, y, z, s=.18):
    rock('Moss', (x, y, z), (s, s * .8, s * .45), 'moss', x + y)


def colonnade(x0, x1, y, h, n):
    for i in range(n):
        x = x0 + (x1 - x0) * i / max(1, n - 1)
        cone('Column', (x, y, h / 2 + .15), .09, h, 'plaster', 8, .08)
        box('Capital', (x, y, h + .17), (.22, .22, .06), 'gold')
    box('Entablature', ((x0 + x1) / 2, y, h + .24), (abs(x1 - x0) + .3, .26, .1), 'stone')


def gable(name, pos, w, d, h, color):
    # Triangular prism roof: ridge along X.
    x, y, z = pos
    verts = [(x - w / 2, y - d / 2, z), (x + w / 2, y - d / 2, z), (x + w / 2, y + d / 2, z), (x - w / 2, y + d / 2, z),
             (x - w / 2, y, z + h), (x + w / 2, y, z + h)]
    faces = [(0, 1, 5, 4), (2, 3, 4, 5), (0, 4, 3), (1, 2, 5), (3, 2, 1, 0)]
    mesh = gw.bpy.data.meshes.new(name)
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = gw.bpy.data.objects.new(name, mesh)
    gw.bpy.context.collection.objects.link(obj)
    return gw.finish(obj, name, color)


def hall(x, y, w, d, h, wall='stone', roof='roof'):
    box('HallWall', (x, y, h / 2 + .17), (w, d, h), wall)
    if wall == 'stone':
        strata(x, y, w, d, h)
    gable('HallRoof', (x, y, h + .17), w * 1.15, d * 1.2, max(.35, h * .6), roof)


def keep(t):
    pad(4)
    h = 1.6 + .5 * t
    # Higher tiers rise on ziggurat steps.
    for s in range(t - 1):
        box('ZigguratStep', (0, .2, .2 + s * .18), (2.2 - s * .3, 2.0 - s * .3, .18), 'stone')
    box('Keep', (0, .2, h / 2 + .12), (1.5, 1.4, h), 'stone')
    strata(0, .2, 1.5, 1.4, h, 1 + t)
    moss(-.7, -.45, .35)
    moss(.72, .85, .5, .14)
    if t >= 2:
        colonnade(-.6, .6, -.75, .9 + .1 * t, 4)
    cone('KeepRoof', (0, .2, h + .12 + .35), 1.25, .7, 'roof', 4, .12, math.pi / 4)
    box('GoldBand', (0, .2, h + .06), (1.75, 1.65, .1), 'gold')
    rune(0, -.52, h * .7, .28)
    for x in (-.35, .35):
        box('Window', (x, -.5, h * .45 + .1), (.16, .04, .34), 'rune')
    towers = [(-1.45, -1.3), (1.45, -1.3), (-1.45, 1.3), (1.45, 1.3)]
    if t >= 2:
        towers += [(0, 1.5)]
    for x, y in towers:
        th = .9 + .35 * t
        cone('Tower', (x, y, th / 2 + .12), .38, th, 'stone', 8, .34)
        cone('TowerCap', (x, y, th + .12 + .22), .48, .45, 'roof', 4, .05, math.pi / 4)
        cone('CapRim', (x, y, th + .1), .4, .07, 'gold', 8, .4)
    for x in (-1.45, 1.45):
        box('SideWall', (x, 0, .42), (.22, 2.4, .6), 'stone')
    box('BackWall', (0, 1.32, .42), (2.7, .22, .6), 'stone')
    for x in (-.85, .85):
        box('FrontWall', (x, -1.32, .42), (.95, .22, .6), 'stone')
    box('Gate', (0, -1.32, .9), (.8, .3, .22), 'gold')
    if t >= 3:
        cone('SealSpire', (0, .2, h + 1.05), .2, .75, 'rune', 5, .03)


def sawmill(t):
    pad(3, 'wood')
    hall(-.3, .35, 1.6, 1.2, .55 + .15 * t, 'timber', 'roofwarm')
    bpy_wheel = cone('SawWheel', (.85, -.1, .55), .42, .1, 'gold', 10, .42)
    bpy_wheel.rotation_euler.x = math.pi / 2
    for i in range(2 + t):
        z = .2 + (i // 3) * .2
        x = -.9 + (i % 3) * .32
        beam('Log', (x, -1.2, z), (x, -.5, z), .13, 'wood', 7)
    if t >= 2:
        hall(.75, .85, .8, .7, .45, 'timber', 'roofwarm')
    if t >= 3:
        box('Crane', (1.1, -.9, .9), (.12, .12, 1.6), 'wood')


def quarry(t):
    pad(3, 'dark')
    cone('Pit', (0, .2, .16), 1.0, .1, 'soil', 8, .8)
    for i in range(2 + t):
        a = i * 2.2
        rock('Strata', (math.cos(a) * .9, math.sin(a) * .8 + .2, .35), (.38, .32, .35 + .05 * t), 'ochre' if i % 2 else 'stone', a)
    for i in range(t + 1):
        box('CutBlock', (-.9 + i * .38, -1.05, .2), (.3, .3, .28), 'ochre')
    if t >= 2:
        beam('CraneMast', (1.1, -.8, .1), (1.1, -.8, 1.6), .06, 'wood')
        beam('CraneArm', (1.1, -.8, 1.55), (.2, .1, 1.3), .045, 'wood')
    rune(-1.1, -1.25, .5, .1, 'gold')


def farm(t):
    pad(3, 'soil')
    beds = 2 + t
    for i in range(beds):
        x = -1.0 + i * (2.0 / max(1, beds - 1))
        box('Bed', (x, -.2, .15), (.32, 1.6, .12), 'wood')
        for y in (-.7, -.2, .3):
            cone('Cap', (x, y, .38), .15, .12, 'fungus', 6, .04)
    hall(.0, 1.05, 1.2 + .2 * t, .6, .45, 'plaster', 'leafroof')
    if t >= 3:
        cone('SporeTower', (1.1, .9, .7), .2, 1.2, 'fungus', 6, .12)


def crystal(t):
    pad(3, 'dark')
    box('MineArch', (0, .8, .55), (1.3, .5, 1.0), 'dark')
    box('ArchGlow', (0, .55, .95), (1.1, .05, .08), 'rune')
    box('MineMouth', (0, .54, .45), (.6, .04, .7), 'dark')
    box('ArchLintel', (0, .6, 1.08), (1.4, .4, .14), 'gold')
    spots = [(-.7, -.3), (.6, -.5), (0, -.9), (-.9, .6), (.95, .4), (.3, .1)]
    for i, (x, y) in enumerate(spots[:2 + t * 1 + (1 if t == 3 else 0)]):
        hgt = .55 + .2 * t - .08 * i
        cone('Crystal', (x, y, hgt * .45), .2 + .04 * t, hgt * .8, 'rune', 5, .14, i)
        cone('CrystalTip', (x, y, hgt * .92), .14, hgt * .25, 'rune', 5, 0, i)
    beam('Rail', (-.3, .4, .06), (-.3, -1.3, .06), .03, 'gold')
    beam('Rail', (.3, .4, .06), (.3, -1.3, .06), .03, 'gold')


def warehouse(t):
    pad(3, 'dark')
    hall(0, .3, 2.0, 1.4, .7 + .25 * t, 'plaster', 'roofwarm')
    box('Door', (0, -.41, .5), (.6, .04, .7), 'wood')
    for i in range(1 + t):
        box('Crate', (-1.0 + i * .45, -1.15, .22), (.36, .36, .36), 'wood')
    if t >= 2:
        box('Annex', (1.15, .8, .45), (.6, .8, .7), 'stone')
    rune(0, -.42, 1.0 + .15 * t, .13, 'gold')


def barracks(t):
    pad(3, 'dark')
    hall(0, .4, 2.0, 1.3, .6 + .2 * t, 'stone', 'crimson')
    for x in (-.6, .6):
        beam('BannerPole', (x, -.3, .1), (x, -.3, 1.3 + .2 * t), .03, 'wood')
        box('Banner', (x + .14, -.3, 1.05 + .2 * t), (.3, .03, .48), 'crimson')
    for i in range(t):
        x = -.9 + i * .6
        beam('Dummy', (x, -1.1, .1), (x, -1.1, .75), .05, 'wood')
        beam('DummyArm', (x - .2, -1.1, .55), (x + .2, -1.1, .55), .035, 'wood')
    if t >= 3:
        cone('Watchtower', (1.15, .9, .8), .3, 1.6, 'stone', 6, .26)
        cone('WatchCap', (1.15, .9, 1.75), .38, .4, 'dark', 4, .05, math.pi / 4)


def hospital(t):
    pad(3, 'dark')
    hall(0, .35, 1.9, 1.3, .6 + .2 * t, 'white', 'rune')
    box('Door', (0, -.32, .45), (.5, .04, .62), 'wood')
    rune(0, -.33, .95 + .2 * t, .18)
    for i in range(1 + t):
        x = -1.0 + i * .5
        box('HerbBed', (x, -1.1, .14), (.4, .4, .1), 'soil')
        cone('Herb', (x, -1.1, .3), .14, .25, 'herb', 6, .03)
    if t >= 2:
        box('Ward', (1.15, .9, .4), (.55, .7, .6), 'white')
    if t >= 3:
        cone('SealLantern', (-1.15, .9, .9), .14, .5, 'rune', 5, .14)


def site():
    pad(3, 'soil')
    for x in (-1.1, 1.1):
        for y in (-1.1, 1.1):
            beam('Stake', (x, y, .05), (x, y, .8), .05, 'wood')
    for y in (-1.1, 1.1):
        beam('Rope', (-1.1, y, .7), (1.1, y, .7), .015, 'gold')
    for x in (-1.1, 1.1):
        beam('Rope', (x, -1.1, .7), (x, 1.1, .7), .015, 'gold')
    for i in range(3):
        box('Stones', (-.4 + i * .35, .2, .15), (.3, .3, .2), 'stone')


BUILDINGS = {'Keep': (keep, 3.9), 'Sawmill': (sawmill, 2.9), 'Quarry': (quarry, 2.9), 'Farm': (farm, 2.9),
             'CrystalMine': (crystal, 2.9), 'Warehouse': (warehouse, 2.9), 'Barracks': (barracks, 2.9),
             'Hospital': (hospital, 2.9)}
GROW = 1.3  # taller than the first pass so buildings read as buildings, not tiles
HEIGHT = {'Keep': (3.1, 3.9, 4.8), 'Sawmill': (1.35, 1.55, 1.9), 'Quarry': (1.0, 1.7, 1.75),
          'Farm': (1.1, 1.2, 1.4), 'CrystalMine': (1.25, 1.45, 1.65), 'Warehouse': (1.4, 1.7, 2.0),
          'Barracks': (1.6, 1.85, 2.1), 'Hospital': (1.35, 1.6, 1.85)}


def main():
    gw.SPECS.clear()
    gw.BUILDERS.clear()
    for name, (builder, width) in BUILDINGS.items():
        for t in (1, 2, 3):
            gw.SPECS[f'{name}_{t}'] = (width, width, HEIGHT[name][t - 1] * GROW, TRIS)
            gw.BUILDERS[f'{name}_{t}'] = (lambda b=builder, tier=t: b(tier))
    gw.SPECS['Site'] = (2.9, 2.9, .8, TRIS)
    gw.BUILDERS['Site'] = site
    gw.OUTPUT.mkdir(parents=True, exist_ok=True)
    gw.REPORTS.mkdir(parents=True, exist_ok=True)
    gw.bpy.context.scene.unit_settings.system = 'METRIC'
    gw.bpy.context.scene.unit_settings.scale_length = 1
    for name in gw.SPECS:
        gw.export(name)


if __name__ == '__main__':
    main()
