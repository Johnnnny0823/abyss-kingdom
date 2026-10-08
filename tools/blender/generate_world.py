"""Original Covenant Ruins models. Blender 5.2, no add-ons or external assets.

blender --background --python tools/blender/generate_world.py -- --batch city
Use --batch terrain, landmarks, or all. Palette and dimensions live below.
"""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'client/Assets/Resources/Models/World'
REPORTS = ROOT / 'artifacts/models'
# Colors are sRGB; convert to linear for Blender's Principled Base Color.
PALETTE = {
    'stone': '#647F96', 'dark': '#243747', 'rune': '#63E0CB', 'gold': '#E5B96C',
    'wood': '#705342', 'leaf': '#346C65', 'leaflight': '#68A894',
    'snow': '#D8E4E1', 'soil': '#424D55', 'bone': '#D7C8AC',
    'fungus': '#DAAF74', 'hostile': '#99505B',
}
# Width, depth, height, maximum triangles. Dimensions are meters/map tiles.
SPECS = {
    'City': (2, 2, 2.5, 3000),
    'Tree_A': (1.2, 1.2, 1.85, 300), 'Tree_B': (1.2, 1.2, 1.65, 300),
    'Mountain_A': (5, 5, 4.6, 300), 'Mountain_B': (5, 5, 5.7, 300),
    'Field_Wood': (2, 2, 1.45, 1500), 'Field_Stone': (2, 2, .95, 1500),
    'Field_Food': (2, 2, .78, 1500), 'Field_Crystal': (2, 2, 1.45, 1500),
    'MonsterCamp': (2, 2, 1.45, 1500), 'PassTower': (1.5, 1.5, 4, 1500),
    'AbyssGate': (6, 6, 3, 3000), 'Troop': (1, 1, 1, 800),
}
BATCHES = {'city': ['City'], 'terrain': list(SPECS)[1:9], 'landmarks': list(SPECS)[9:]}
MATERIALS = {}


def material(key):
    if key not in MATERIALS:
        rgb = [int(PALETTE[key][i:i + 2], 16) / 255 for i in (1, 3, 5)]
        linear = [c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4 for c in rgb]
        mat = bpy.data.materials.new('Covenant_' + key)
        mat.diffuse_color = (*linear, 1)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Base Color'].default_value = (*linear, 1)
        bsdf.inputs['Roughness'].default_value = .85
        MATERIALS[key] = mat
    return MATERIALS[key]


def finish(obj, name, color):
    obj.name = name
    obj.data.materials.append(material(color))
    return obj


def box(name, pos, size, color, rot=0):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    obj.rotation_euler.z = rot
    return finish(obj, name, color)


def cone(name, pos, radius, height, color, sides=6, top=0, rot=0):
    bpy.ops.mesh.primitive_cone_add(vertices=sides, radius1=radius, radius2=top,
                                  depth=height, location=pos, rotation=(0, 0, rot))
    return finish(bpy.context.object, name, color)


def rock(name, pos, size, color, angle=0):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    obj.rotation_euler.z = angle
    return finish(obj, name, color)


def beam(name, a, b, width, color, sides=4):
    delta = Vector(b) - Vector(a)
    obj = cone(name, (Vector(a) + Vector(b)) / 2, width, delta.length, color, sides, width)
    obj.rotation_euler = delta.to_track_quat('Z', 'Y').to_euler()
    return obj


def rune(x, y, z, size=.15, color='rune'):
    # Broken lozenge seal, an original recurring covenant motif. Faces -Y.
    for a, b in [((0, 1), (.7, 0)), ((.7, 0), (0, -1)), ((0, -1), (-.7, 0))]:
        beam('ContractSeal', (x + a[0] * size, y, z + a[1] * size),
             (x + b[0] * size, y, z + b[1] * size), size * .12, color)


def platform(size=2, color='dark'):
    obj = cone('Foundation', (0, 0, .055), size * .54, .11, color, 8, size * .51, math.pi / 8)
    obj.scale.y = .95


def city():
    platform()
    box('Citadel', (0, .18, .88), (.9, .86, 1.5), 'stone')
    cone('CitadelRoof', (0, .18, 1.84), .81, .48, 'dark', 4, .1, math.pi / 4)
    box('RoofGoldBand', (0, .18, 1.61), (1.18, 1.18, .075), 'gold')
    cone('SealSpire', (0, .18, 2.24), .16, .49, 'rune', 5, .025)
    for x in (-.72, .72):
        for y in (-.65, .65):
            cone('WatchTower', (x, y, .56), .24, .92, 'stone', 8, .21)
            cone('TowerCrown', (x, y, 1.13), .34, .34, 'dark', 4, .06, math.pi / 4)
            cone('CrownRim', (x, y, .99), .25, .06, 'gold', 8, .25)
        box('SideWall', (x, 0, .38), (.14, 1.35, .54), 'stone')
        for y in (-.36, 0, .36):
            box('Merlon', (x, y, .72), (.19, .18, .16), 'stone')
    box('BackWall', (0, .72, .38), (1.4, .14, .54), 'stone')
    for x in (-.45, .45):
        box('GateWall', (x, -.75, .4), (.43, .18, .6), 'stone')
    box('GateLintel', (0, -.75, .84), (.55, .24, .2), 'gold')
    box('GateRecess', (0, -.65, .42), (.34, .04, .59), 'dark')
    for i in range(3):
        box('FrontSteps', (0, -.85 + i * .1, .04 + i * .035), (.53, .24, .08 + i * .07), 'stone')
    rune(0, -.264, 1.3, .22)
    rune(0, -.89, .83, .075)
    for x in (-.28, .28):
        box('Window', (x, -.255, .96), (.1, .035, .24), 'rune')


def tree(x=0, y=0, height=1.8, variant=0):
    cone('RootTrunk', (x, y, height * .26), .13, height * .52, 'wood', 5, .07)
    for angle in (0, 2.1, 4.2):
        beam('ButtressRoot', (x + math.cos(angle) * .29, y + math.sin(angle) * .29, .03),
             (x, y, height * .3), .055, 'wood')
    if variant == 0:
        for h, r in ((.49, .59), (.70, .46), (.87, .3)):
            cone('FacetedCrown', (x, y, height * h), r * height / 1.8, height * .42,
                 'leaflight' if h > .8 else 'leaf', 7, .025, h)
    else:
        for dx, dy, dz, radius in ((-.24, 0, .67, .48), (.25, .08, .77, .45), (0, -.2, .91, .32)):
            rock('RootwoodCrown', (x + dx * height / 1.8, y + dy, height * dz),
                 (radius, radius, height * .24), 'leaflight' if dz > .8 else 'leaf', dz)


def mountain(variant=0):
    # Offset layered polygon rings, rather than a uniformly scaled primitive.
    n = 9
    verts = []
    rings = [(0, 2.6), (1.15, 1.95), (2.7, 1.05), (3.7, .47)]
    for k, (z, r) in enumerate(rings):
        for i in range(n):
            a = i * math.tau / n
            rr = r * (1 + .17 * math.sin(i * 5 + variant * 3 + k))
            verts.append((math.cos(a) * rr + k * .1, math.sin(a) * rr - k * .12,
                          z + (0 if k == 0 else .24 * math.sin(i * 2 + k))))
    verts.append((.25, -.44, 4.7 if variant == 0 else 5.7))
    faces = [tuple(reversed(range(n)))]
    for k in range(3):
        for i in range(n):
            a, b = k * n + i, k * n + (i + 1) % n
            faces.extend([(a, b, b + n), (a, b + n, a + n)])
    faces += [(27 + i, 27 + (i + 1) % n, 36) for i in range(n)]
    mesh = bpy.data.meshes.new('StratifiedRidge')
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = bpy.data.objects.new('StratifiedRidge', mesh)
    bpy.context.collection.objects.link(obj)
    for key in ('dark', 'stone', 'snow'):
        mesh.materials.append(material(key))
    for p in mesh.polygons:
        p.material_index = 2 if p.center.z > 3.4 else (p.index % 3 == 0)
    rock('Foothill', (-1.1, .5, .6), (1.2, 1, .9), 'stone')


def field_wood():
    platform(color='wood')
    for x, y, h in ((-.55, .35, 1.35), (.45, .53, 1.1)):
        tree(x, y, h)
    for x, z in ((-.32, .22), (0, .22), (.32, .22), (-.16, .46), (.16, .46)):
        beam('TimberStack', (x, -.85, z), (x, -.05, z), .13, 'wood', 7)
        cone('CutEnd', (x, -.859, z), .095, .016, 'gold', 7, .095).rotation_euler.x = math.pi / 2


def field_stone():
    platform(color='dark')
    for x, y, z, s in ((-.4, .4, .35, .6), (.4, .3, .35, .55), (-.6, -.45, .18, .3)):
        rock('QuarryStrata', (x, y, z), (s, s * .85, s * .8), 'stone', x)
    for i in range(3):
        box('CutStone', (.25, -.55 + i * .22, .2), (.65, .18, .24), 'stone')
    box('ContractMarker', (-.69, .05, .44), (.18, .18, .75), 'dark')
    rune(-.69, -.051, .65, .085, 'gold')


def field_food():
    platform(color='soil')
    for x in (-.56, -.1, .36):
        box('CultivationBed', (x, -.18, .14), (.37, 1.2, .12), 'wood')
        for y in (-.56, -.12, .29):
            h = .32 + .06 * math.sin(x * 7 + y * 5)
            cone('MushroomStem', (x, y, h), .045, h, 'fungus', 5, .05)
            cone('MushroomCap', (x, y, h * 1.5), .18, .14, 'rune', 7, .045)
    for x in (-.74, .65):
        box('CanopyPost', (x, .65, .36), (.08, .08, .6), 'wood')
    box('Canopy', (-.045, .66, .72), (1.65, .42, .1), 'fungus')


def field_crystal():
    platform(color='dark')
    for i, (x, y, h, r) in enumerate(((0, .2, 1.35, .24), (-.46, .17, .91, .2),
                                   (.48, .35, .8, .2), (.17, -.42, .66, .19), (-.45, -.51, .5, .17))):
        cone('CrystalShaft', (x, y, h * .4), r, h * .63, 'rune', 5, r * .85, i)
        cone('CrystalTip', (x, y, h * .84), r * .85, h * .28, 'rune', 5, 0, i)
        rock('CrystalHost', (x, y, .12), (r * 1.5, r * 1.5, .2), 'stone', i)
    for x in (-.8, .8):
        box('ClaimTablet', (x, -.5, .27), (.13, .14, .45), 'gold')


def camp():
    platform(color='dark')
    for x, y, s in ((-.4, .22, .75), (.52, .4, .52)):
        tent = cone('RaggedTent', (x, y, .52 * s), .65 * s, 1.05 * s, 'hostile', 4, .04, math.pi / 4)
        tent.scale.y = 1.25
        box('TentOpening', (x, y - .44 * s, .25), (.24 * s, .035, .38), 'dark')
    for x in (-.8, .8):
        beam('BoneStake', (x, -.66, .07), (x * .9, -.62, 1), .055, 'bone', 5)
        cone('StakeTip', (x * .9, -.62, 1.05), .085, .25, 'bone', 5)
    beam('BoneLintel', (-.72, -.62, .86), (.72, -.62, .86), .04, 'bone', 5)
    rock('SkullSeal', (0, -.64, .86), (.17, .08, .14), 'bone')
    for x in (-.055, .055):
        box('SkullSocket', (x, -.716, .885), (.04, .01, .05), 'dark')
    beam('BrokenStandard', (.7, 0, .1), (.7, 0, 1.44), .028, 'bone')
    mesh = bpy.data.meshes.new('TornBanner')
    mesh.from_pydata([(.7, 0, 1.37), (.15, 0, 1.27), (.3, 0, 1.04), (.48, 0, 1.11), (.7, 0, 1.03)], [], [(0, 1, 2, 3, 4), (4, 3, 2, 1, 0)])
    obj = bpy.data.objects.new('TornBanner', mesh)
    bpy.context.collection.objects.link(obj)
    finish(obj, 'TornBanner', 'hostile')


def tower():
    platform(1.5)
    cone('BatteredTower', (0, 0, 1.5), .67, 2.8, 'stone', 4, .46, math.pi / 4)
    for z in (.35, 1.4, 2.65):
        box('StrataCourse', (0, 0, z), (1.1, 1.1, .12), 'dark')
    box('CrownDeck', (0, 0, 2.97), (1.35, 1.35, .2), 'gold')
    for x in (-.48, .48):
        for y in (-.48, .48):
            box('CrownTooth', (x, y, 3.3), (.26, .26, .55), 'stone')
    cone('Beacon', (0, 0, 3.65), .24, .68, 'rune', 5, 0)
    box('SealRecess', (0, -.438, 2.12), (.44, .07, .78), 'dark')
    rune(0, -.485, 2.12, .26)
    box('ArrowSlit', (0, -.535, .88), (.13, .02, .46), 'dark')


def gate():
    # Concentric masonry surrounds a dark well; broken pylons frame the rear.
    cone('WellFoundation', (0, 0, .1), 3, .2, 'dark', 12, 3)
    cone('Abyss', (0, 0, .22), 1.86, .035, 'dark', 12, 1.86)
    for i in range(12):
        a = i * math.tau / 12
        box('RimStone', (2.15 * math.cos(a), 2.15 * math.sin(a), .36), (.84, .48, .42), 'stone', a + math.pi / 2)
        box('SealInlay', (1.87 * math.cos(a), 1.87 * math.sin(a), .58), (.23, .055, .045), 'rune', a)
    for x in (-1.65, 1.65):
        box('AncientPylon', (x, .85, 1.5), (.55, .65, 2.8), 'stone')
        box('PylonCapital', (x, .85, 2.83), (.86, .9, .25), 'gold')
        rune(x, .512, 2.15, .3)
        box('PylonFoot', (x, .85, .44), (.92, .95, .55), 'stone')
    box('BrokenLintelLeft', (-.91, .85, 2.73), (1.35, .64, .34), 'stone', -.06)
    box('BrokenLintelRight', (.99, .85, 2.78), (1.16, .64, .34), 'stone', .07)
    rock('SuspendedKeystone', (0, .85, 2.63), (.23, .22, .35), 'rune')
    for i in range(4):
        box('ApproachSteps', (0, -2.7 + i * .23, .06 + i * .05), (1.35, .4, .12 + i * .1), 'stone')


def troop():
    for x, y in ((0, -.29), (-.29, .19), (.29, .19)):
        for dx in (-.065, .065):
            box('Boot', (x + dx, y, .12), (.1, .15, .24), 'dark')
        cone('Armor', (x, y, .4), .18, .33, 'stone', 5, .12)
        rock('Helmet', (x, y, .67), (.15, .13, .17), 'stone')
        box('Visor', (x, y - .125, .66), (.17, .023, .043), 'dark')
        box('Shield', (x - .13, y - .16, .4), (.21, .06, .3), 'dark')
        rune(x - .13, y - .2, .4, .073, 'rune')
        beam('Spear', (x + .17, y, .1), (x + .17, y, .89), .017, 'gold')
        cone('Spearhead', (x + .17, y, .95), .045, .1, 'gold', 4)


BUILDERS = {'City': city, 'Tree_A': tree, 'Tree_B': lambda: tree(variant=1),
            'Mountain_A': mountain, 'Mountain_B': lambda: mountain(1),
            'Field_Wood': field_wood, 'Field_Stone': field_stone, 'Field_Food': field_food,
            'Field_Crystal': field_crystal, 'MonsterCamp': camp, 'PassTower': tower,
            'AbyssGate': gate, 'Troop': troop}


def export(name):
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    BUILDERS[name]()
    objects = list(bpy.context.scene.objects)
    bpy.ops.object.select_all(action='SELECT')
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    coords = [v.co.copy() for v in obj.data.vertices]
    low = Vector(tuple(min(v[i] for v in coords) for i in range(3)))
    high = Vector(tuple(max(v[i] for v in coords) for i in range(3)))
    target = Vector(SPECS[name][:3])
    size = high - low
    center = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    for vertex in obj.data.vertices:
        p = vertex.co - center
        vertex.co = Vector(tuple(p[i] * target[i] / size[i] for i in range(3)))
    bpy.context.scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    # Unity's FBX conversion maps authoring -Y to +Z. Face the south camera (-Z).
    obj.rotation_euler.z = math.pi
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    obj.data.calc_loop_triangles()
    tris = len(obj.data.loop_triangles)
    mats = len(obj.data.materials)
    assert tris <= SPECS[name][3], (name, tris)
    assert 1 <= mats <= 4, (name, mats)
    assert min(v.co.z for v in obj.data.vertices) >= -1e-6
    # Explicit triangulation gives the same face budget in Blender and Unity.
    mod = obj.modifiers.new('ExportTriangles', 'TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=mod.name)
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT / (name + '.fbx')), use_selection=True,
        object_types={'MESH'}, apply_scale_options='FBX_SCALE_UNITS', axis_forward='-Z', axis_up='Y',
        bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE',
        add_leaf_bones=False, bake_anim=False, path_mode='COPY', embed_textures=True)
    record = {'name': name, 'dimensions_m': list(target), 'triangles': tris,
              'triangle_limit': SPECS[name][3], 'materials': mats, 'origin': [0, 0, 0],
              'front': 'Authored -Y, rotated Z 180 degrees for Unity -Z', 'palette': PALETTE}
    (REPORTS / (name + '.json')).write_text(json.dumps(record, indent=2) + '\n')
    print('MODEL_VALIDATED ' + json.dumps(record))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--batch', choices=[*BATCHES, 'all'], default='all')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    OUTPUT.mkdir(parents=True, exist_ok=True)
    REPORTS.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1
    for name in SPECS if args.batch == 'all' else BATCHES[args.batch]:
        export(name)


if __name__ == '__main__':
    main()
