"""Rebuild accepted geometry with isolated cloth and chimney sockets. Blender only."""
import sys, math, json, random
sys.dont_write_bytecode = True
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0, str(Path(__file__).resolve().parent))
import city3d_common as c
import generate_city3d as g

PREVIEW = c.ROOT / 'artifacts/building_polish'
FLAGS = []
SOCKETS = []
original_flag = c.flag


def flag(x, y, z, w=.13, h=.065):
    start = len(c.PARTS)
    original_flag(x, y, z, w, h)
    bpy.context.view_layer.update()
    name = 'FlagCloth_%02d' % len(FLAGS)
    FLAGS.append(name)
    for obj in c.PARTS[start:]:
        if not obj.name.startswith(('RoyalFlag', 'FlagBorder')):
            continue
        group = obj.vertex_groups.new(name=name)
        group.add(list(range(len(obj.data.vertices))), 1, 'REPLACE')
        colors = obj.data.color_attributes.new(name='ClothWeight', type='FLOAT_COLOR', domain='POINT')
        for v in obj.data.vertices:
            p = obj.matrix_world @ v.co
            # The attached edge never moves; both gold border and fabric share this mask.
            weight = max(0, min(1, (p.x-x)/w))
            colors.data[v.index].color = (weight, 0, 0, 1)


def chimney(x, y, bottom, top):
    c.box('HearthChimney', (x,y,(bottom+top)/2), (.070,.075,top-bottom), 'stone')
    c.box('ChimneyCollar', (x,y,top-.030), (.095,.100,.022), 'light')
    for dx,dy,w,d in [(-.037,0,.019,.094),(.037,0,.019,.094),(0,-.038,.055,.019),(0,.038,.055,.019)]:
        c.box('OpenChimneyRim',(x+dx,y+dy,top), (w,d,.026),'light')
    c.box('ChimneyThroat',(x,y,top-.009),(.055,.057,.008),'dark')
    SOCKETS.append((x,y,top+.014))


def keep_one():
    # Execute only the original modeling statements, never its legacy export/file operations.
    c.reset('blue'); random.seed(19)
    source = (Path(__file__).parent/'keep_1_trial.py').read_text(encoding='utf-8')
    body = source.split('# Terrace and entrance stairs, all within a one-unit footprint.')[1].split('# One mesh, one UV atlas, one exported material.')[0]
    scope = dict(vars(c)); scope['flag'] = flag
    exec(compile(body, 'keep_1_geometry', 'exec'), scope)


def split_details(obj, minimum, maximum, scale):
    root = bpy.data.objects.new(ASSET, None); bpy.context.collection.objects.link(root)
    obj.name = 'BuildingBody'; obj.parent = root
    objects = [root, obj]
    for name in FLAGS:
        bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        group = obj.vertex_groups.get(name)
        assert group is not None, name
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT')
        bpy.ops.object.mode_set(mode='OBJECT')
        for v in obj.data.vertices:
            v.select = any(a.group == group.index for a in v.groups)
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.separate(type='SELECTED'); bpy.ops.object.mode_set(mode='OBJECT')
        cloth = next(o for o in bpy.context.selected_objects if o != obj)
        cloth.name = name; cloth.parent = root
        objects.append(cloth)
    for i, p in enumerate(SOCKETS):
        marker = bpy.data.objects.new('SmokeSocket_%02d' % i, None)
        bpy.context.collection.objects.link(marker); marker.parent = root
        marker.location = ((p[0]-(maximum[0]+minimum[0])/2)*scale[0],
                           (p[1]-(maximum[1]+minimum[1])/2)*scale[1],(p[2]-minimum[2])*scale[2])
        objects.append(marker)
    return objects


def build(family, tier):
    global ASSET
    ASSET = f'{family}_{tier}'; FLAGS.clear(); SOCKETS.clear()
    c.flag = g.flag = flag
    if ASSET == 'Keep_1': keep_one()
    else: g.BUILDERS[family](tier)
    # Hearths belong to occupied buildings, not open mines, yards or empty sites.
    if family == 'Keep': chimney(-.15,-.17,.65,.99 if tier==1 else 1.16+(tier-2)*.15)
    elif family == 'Sawmill': SOCKETS.append((-.29,-.27,.835+.10*(tier-1)))
    elif family == 'Farm': chimney(-.29,-.27,.38,.67+.13*(tier-1) if tier<3 else .75)
    elif family == 'Hospital': chimney(.31,-.25,.36,.64+.025*(tier-1))
    elif family == 'Barracks': chimney(-.24,-.27,.40,.80+.10*(tier-1))
    # Extra pennants identify working premises without putting flags on every small prop.
    if family in ('Sawmill','Warehouse','Hospital'):
        flag(.37,.26,.41+.06*(tier-1),.095,.055)
    c.PREVIEWS = PREVIEW
    height = 1.307 if ASSET=='Keep_1' else g.HEIGHTS[family][tier]
    report = c.finish_asset(ASSET, height, split_details)
    report['flags'] = list(FLAGS)
    report['smoke_sockets'] = [o.name for o in bpy.context.scene.objects if o.name.startswith('SmokeSocket_')]
    (PREVIEW/(ASSET+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('POLISH_DONE', ASSET, flush=True)


if __name__ == '__main__':
    families = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['Keep','Sawmill','Farm','Warehouse','Barracks','Hospital','CrystalMine']
    for family in families:
        for tier in (1,2,3): build(family,tier)
