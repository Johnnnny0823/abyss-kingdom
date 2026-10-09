"""Keep_1 reference-driven mesh trial; no external dependencies or AI services.
Run with Blender --background --factory-startup --python this_file.py.
Blender +Y is the entrance; FBX conversion maps it to Unity -Z.
"""
import bpy
import math
import json
import random
import shutil
import time
from pathlib import Path
from mathutils import Vector

START = time.monotonic()
bpy.context.preferences.filepaths.save_version = 0
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Assets/Resources/Models/City'
ART = ROOT / 'artifacts/keep_1_trial'
BACKUP = ROOT / 'art/source/models-greybox-backup'
for directory in (OUT, ART, BACKUP):
    directory.mkdir(parents=True, exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
random.seed(19)
PARTS = []


def color(hex_value):
    values = [int(hex_value[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values) + (1,)


def material(name, a, b=None, brick=False):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    emit = nodes.new('ShaderNodeEmission')
    emit.inputs['Color'].default_value = color(a)
    links.new(emit.outputs[0], output.inputs['Surface'])
    if b:
        geo = nodes.new('ShaderNodeNewGeometry')
        sep = nodes.new('ShaderNodeSeparateXYZ')
        links.new(geo.outputs['Position'], sep.inputs[0])
        normal = nodes.new('ShaderNodeSeparateXYZ')
        links.new(geo.outputs['Normal'], normal.inputs[0])
        absolute = nodes.new('ShaderNodeMath'); absolute.operation = 'ABSOLUTE'
        links.new(normal.outputs['X'], absolute.inputs[0])
        switch = nodes.new('ShaderNodeMath'); switch.operation = 'GREATER_THAN'
        switch.inputs[1].default_value = .4
        links.new(absolute.outputs[0], switch.inputs[0])
        mix = nodes.new('ShaderNodeMixRGB')
        links.new(switch.outputs[0], mix.inputs[0])
        links.new(sep.outputs['X'], mix.inputs[1])
        links.new(sep.outputs['Y'], mix.inputs[2])
        coord = nodes.new('ShaderNodeCombineXYZ')
        links.new(mix.outputs[0], coord.inputs['X'])
        links.new(sep.outputs['Z'], coord.inputs['Y'])
        noise = nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value = 180
        noise.inputs['Detail'].default_value = 2
        links.new(geo.outputs['Position'], noise.inputs['Vector'])
        ramp = nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color = color(a)
        ramp.color_ramp.elements[1].color = color(b)
        links.new(noise.outputs['Fac'], ramp.inputs[0])
        if brick:
            tex = nodes.new('ShaderNodeTexBrick')
            tex.inputs['Scale'].default_value = 1
            tex.inputs['Mortar Size'].default_value = .0015
            tex.inputs['Mortar Smooth'].default_value = .0005
            tex.inputs['Brick Width'].default_value = .065 if name != 'Slate' else .05
            tex.inputs['Row Height'].default_value = .030 if name != 'Slate' else .022
            tex.inputs['Color2'].default_value = color(b)
            tex.inputs['Mortar'].default_value = color('343943' if name != 'Slate' else '142340')
            links.new(coord.outputs[0], tex.inputs['Vector'])
            links.new(ramp.outputs[0], tex.inputs['Color1'])
            links.new(tex.outputs['Color'], emit.inputs['Color'])
        else:
            links.new(ramp.outputs[0], emit.inputs['Color'])
    return mat


M = {
    'stone': material('Masonry', '777D80', 'A0A29B', True),
    'light': material('CutStone', '9F9C89', 'C4BCA0'),
    'dark': material('Recess', '272E36'),
    'roof': material('Slate', '204991', '3869B1', True),
    'blue': material('Banner', '254990', '3966B7'),
    'gold': material('OchreTrim', 'AE7E32', 'E2BD65'),
    'wood': material('Oak', '664322', 'AE7839'),
    'amber': material('Window', 'F6B647'),
    'teal': material('Seal', '37BCAE'),
    'rune': material('RuneInk', 'B8FFF1'),
    'moss': material('Moss', '49612F', '79913F'),
}


def finish(obj, name, mat):
    obj.name = name
    obj.data.materials.append(M[mat])
    PARTS.append(obj)
    return obj


def mesh(name, verts, faces, mat):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    return finish(obj, name, mat)


def box(name, pos, size, mat='stone'):
    bpy.ops.mesh.primitive_cube_add(size=1, location=pos)
    obj = bpy.context.object
    obj.scale = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return finish(obj, name, mat)


def beam(name, a, b, radius=.006, mat='gold', vertices=6):
    delta = Vector(b) - Vector(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=delta.length, location=(Vector(a) + Vector(b)) / 2)
    obj = bpy.context.object
    obj.rotation_euler = delta.to_track_quat('Z', 'Y').to_euler()
    return finish(obj, name, mat)


def hip(name, x, y, z, w, d, h):
    corners = [(x-w/2,y-d/2,z),(x+w/2,y-d/2,z),(x+w/2,y+d/2,z),(x-w/2,y+d/2,z)]
    top = (x,y,z+h)
    mesh(name, corners+[top], [(0,3,2,1),(0,1,4),(1,2,4),(2,3,4),(3,0,4)], 'roof')
    box(name+'_Eave', (x,y,z-.008), (w+.008,d+.008,.015), 'gold')
    for p in corners:
        beam(name+'_Ridge', p, top, .005)


def panel(name, x, y, z, w, h, mat, depth=.007):
    # Pointed arch prism, outward front is +Y.
    outline = [(-w/2,0),(w/2,0),(w/2,h*.72),(0,h),(-w/2,h*.72)]
    verts = [(x+px,y+dy,z+pz) for dy in (-depth/2,depth/2) for px,pz in outline]
    faces = [(4,3,2,1,0),(5,6,7,8,9)]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)]
    return mesh(name, verts, faces, mat)


def window(x,y,z,scale=1):
    panel('WindowFrame',x,y,z,.039*scale,.100*scale,'light')
    panel('WindowRecess',x,y+.005,z+.009*scale,.026*scale,.077*scale,'dark')
    panel('WarmWindow',x,y+.010,z+.012*scale,.012*scale,.066*scale,'amber')


def diamond(name,x,y,z,w,h,mat):
    return mesh(name,[(x,y,z-h/2),(x+w/2,y,z),(x,y,z+h/2),(x-w/2,y,z)],[(0,1,2,3)],mat)


def flag(x,y,z,w=.13,h=.065):
    beam('FlagPole',(x,y,z-.05),(x,y,z+.14),.0045)
    bpy.ops.mesh.primitive_cone_add(vertices=4,radius1=.012,depth=.032,location=(x,y,z+.155))
    finish(bpy.context.object,'Finial','gold')
    verts=[]
    for row in range(2):
        for i in range(9):
            t=i/8
            verts.append((x+w*t,y+.013*math.sin(t*math.pi*2),z+.105-row*h-.018*math.sin(t*math.pi)))
    faces=[(i,i+1,i+10,i+9) for i in range(8)]
    obj=mesh('RoyalFlag',verts,faces,'blue')
    # Two-sided geometry remains visible with backface culling in the target engine.
    solid=obj.modifiers.new('ClothThickness','SOLIDIFY'); solid.thickness=.001
    bpy.context.view_layer.objects.active=obj; bpy.ops.object.modifier_apply(modifier=solid.name)
    for row in range(2):
        for i in range(8):
            beam('FlagBorder',verts[row*9+i],verts[row*9+i+1],.0017,vertices=4)


# Terrace and entrance stairs, all within a one-unit footprint.
box('Foundation',(0,0,.033),(1,.98,.066))
box('Terrace',(0,-.035,.085),(.94,.86,.05),'light')
for i in range(6):
    box('Stair',(0,.48-i*.021,.015+i*.016),(.28,.043,.03+i*.032),'light')
for x in (-.43,.43):
    for y in (-.37,.12,.36):
        box('ButtressFoot',(x,y,.13),(.10,.12,.14))

# Curtain wall and corner towers.
for x in (-.365,.365):
    box('SideCurtain',(x,-.015,.265),(.072,.69,.32))
    box('WallCornice',(x,-.015,.425),(.09,.70,.022),'light')
    for i in range(8):
        box('SideMerlon',(x,-.31+i*.085,.46),(.09,.048,.055),'light')
box('BackCurtain',(0,-.35,.265),(.70,.075,.32))
for i in range(9):
    box('BackMerlon',(-.32+i*.08,-.35,.46),(.047,.09,.06),'light')
for x in (-.245,.245):
    box('FrontCurtain',(x,.32,.28),(.25,.075,.34))
    for dx in (-.084,0,.084):
        box('FrontMerlon',(x+dx,.32,.475),(.05,.094,.07),'light')
for x in (-.365,.365):
    for y in (-.32,.32):
        box('CornerTower',(x,y,.305),(.14,.14,.39))
        box('TowerPlinth',(x,y,.145),(.166,.166,.05),'light')
        box('TowerCornice',(x,y,.51),(.168,.168,.04),'light')
        for dx in (-.063,.063):
            for dy in (-.063,.063):
                box('TowerMerlon',(x+dx,y+dy,.547),(.05,.05,.05),'light')
        hip('TowerRoof',x,y,.55,.119,.119,.11)
        window(x,y+.074,.265,.75)
        if y>0:
            flag(x,y,.70,.095,.048)

# Taller central keep and a rear annex.
box('KeepBody',(0,-.075,.475),(.43,.40,.65))
box('KeepBase',(0,-.075,.24),(.455,.425,.06),'light')
box('KeepCornice',(0,-.075,.80),(.465,.435,.038),'light')
for x in (-.198,.198):
    for y in (-.257,.107):
        box('KeepPier',(x,y,.53),(.056,.052,.52))
        for z in (.36,.51,.66,.77):
            box('Quoin',(x,y,z),(.067,.063,.022),'light')
for x in (-.13,0,.13):
    window(x,.130,.62,1.02)
    # Matching inferred rear windows.
    obj=panel('RearWindow',x,-.281,.62,.032,.083,'amber')
for side in (-1,1):
    for y in (-.17,0):
        obj=panel('SideWindow',0,0,0,.033,.095,'amber')
        obj.rotation_euler.z=side*math.pi/2
        obj.location=(side*.217,y,.62)
hip('MainRoof',0,-.075,.825,.54,.51,.255)
box('RoofCrown',(0,-.075,1.077),(.075,.065,.02),'gold')
flag(0,-.075,1.12,.18,.074)
box('Annex',(.247,-.12,.36),(.12,.24,.36))
hip('AnnexRoof',.247,-.12,.55,.16,.29,.12)
# Main front dormer.
panel('Dormer',0,.17,.88,.075,.113,'gold',.045)
panel('DormerInset',0,.195,.892,.048,.075,'dark')
panel('DormerLight',0,.200,.901,.012,.058,'amber')

# Gate, separate arch stones, banners and cyan contract seal.
panel('GateRecess',0,.365,.118,.225,.305,'dark',.06)
panel('OakGate',0,.400,.118,.183,.266,'wood',.01)
for i in range(8):
    x=-.077+i*.022
    top=.118+.266*(1-.28*abs(x)/.0915)
    beam('PlankJoint',(x,.407,.12),(x,.407,top),.0015,'dark',4)
for x in (-.117,.117):
    for i in range(5):
        box('GateJamb',(x,.395,.14+i*.038),(.043,.08,.036),'light')
for side in (-1,1):
    a=Vector((side*.117,.397,.327)); b=Vector((0,.397,.451))
    for i in range(4):
        p=a.lerp(b,(i+.5)/4)
        obj=box('ArchStone',p,(.047,.079,.039),'light')
        obj.rotation_euler.y=side*-.76
beam('DoorBand',(-.088,.415,.232),(0,.415,.285),.007)
box('ArchKeystone',(0,.400,.428),(.035,.083,.055),'light')
beam('DoorBand',(0,.415,.285),(.088,.415,.232),.007)
diamond('DoorEmblem',0,.424,.225,.035,.053,'gold')
for x in (-.19,.19):
    panel('BannerBorder',x,.367,.183,.076,.255,'gold')
    panel('BannerCloth',x,.373,.193,.057,.230,'blue')
    beam('BannerRail',(x-.049,.385,.448),(x+.049,.385,.448),.008)
    diamond('BannerSeal',x,.382,.285,.021,.039,'gold')
diamond('SealStone',0,.393,.542,.164,.213,'dark')
diamond('SealGold',0,.401,.542,.149,.197,'gold')
diamond('SealTeal',0,.407,.542,.119,.160,'teal')
diamond('SealInterior',0,.409,.542,.079,.115,'dark')
points=[(-.005,.412,.584),(.028,.412,.548),(.008,.412,.517),(-.024,.412,.548),(-.005,.412,.572)]
for a,b in zip(points,points[1:]):
    beam('ContractRune',a,b,.004,'rune',4)
for x in (-.16,.16):
    box('StairPost',(x,.405,.135),(.047,.047,.19),'light')
    hip('PostCap',x,.405,.238,.06,.06,.033)

# Sparse low-poly moss clusters, not a separate transparency texture.
for i in range(30):
    side=-1 if i%2 else 1
    x=side*random.uniform(.30,.46); y=random.uniform(-.38,.36)
    z=random.choice([.075,.13,.18])
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=(x,y,z))
    obj=bpy.context.object; obj.scale=(.018,.022,.009)
    finish(obj,'MossCluster','moss')

# One mesh, one UV atlas, one exported material.
bpy.ops.object.select_all(action='DESELECT')
for obj in PARTS:
    obj.select_set(True)
bpy.context.view_layer.objects.active=PARTS[0]
bpy.ops.object.join()
obj=bpy.context.object; obj.name='Keep_1'; obj.data.name='Keep_1_Mesh'
bpy.context.scene.cursor.location=(0,0,0)
bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
bpy.ops.object.mode_set(mode='EDIT')
bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.mesh.normals_make_consistent(inside=False)
bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.006)
bpy.ops.object.mode_set(mode='OBJECT')
mod=obj.modifiers.new('Triangles','TRIANGULATE')
bpy.ops.object.modifier_apply(modifier=mod.name)
tris=len(obj.data.polygons)
assert tris<=5000, f'Triangle budget exceeded: {tris}'
for loop in obj.data.uv_layers.active.data:
    loop.uv.x *= .93
atlas=bpy.data.images.new('Keep_1_Albedo',width=1024,height=1024,alpha=False)
atlas.colorspace_settings.name='sRGB'
for mat in obj.data.materials:
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=atlas
    mat.node_tree.nodes.active=tex
scene=bpy.context.scene
scene.render.engine='CYCLES'; scene.cycles.samples=8
scene.render.bake.margin=4
bpy.ops.object.bake(type='EMIT')
# Reserve solid-color swatches for subpixel trim; avoid black bake seams on thin beams.
swatches={'OchreTrim':'D1A34E','Recess':'272E36','Window':'F6B647',
          'RuneInk':'B8FFF1','Seal':'37BCAE','Banner':'31589F','Moss':'627D37'}
pixels=list(atlas.pixels[:])
for slot,(name,hex_value) in enumerate(swatches.items()):
    rgba=tuple(int(hex_value[i:i+2],16)/255 for i in (0,2,4))+(1,)
    for y in range(slot*128,(slot+1)*128):
        for x in range(960,1024):
            at=(y*1024+x)*4
            pixels[at:at+4]=rgba
    for face in obj.data.polygons:
        if obj.data.materials[face.material_index].name==name:
            for index in face.loop_indices:
                obj.data.uv_layers.active.data[index].uv=(992/1024,(slot*128+64)/1024)
atlas.pixels[:]=pixels
atlas.filepath_raw=str(OUT/'Keep_1_Albedo.png'); atlas.file_format='PNG'; atlas.save()
final=bpy.data.materials.new('Keep_1_Matte'); final.use_nodes=True
bsdf=final.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Metallic'].default_value=0
bsdf.inputs['Roughness'].default_value=1
bsdf.inputs['Specular IOR Level'].default_value=0
tex=final.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=atlas
final.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
obj.data.materials.clear(); obj.data.materials.append(final)
for face in obj.data.polygons:
    face.material_index=0

# Backup before replacing the single requested model; preserve its Unity GUID.
for suffix in ('.fbx','.fbx.meta'):
    source=OUT/('Keep_1'+suffix); target=BACKUP/source.name
    if source.exists() and not target.exists():
        shutil.copy2(source,target)
scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
bpy.ops.export_scene.fbx(filepath=str(OUT/'Keep_1.fbx'),use_selection=True,object_types={'MESH'},
    axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
    bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,
    path_mode='COPY',embed_textures=True)

# Front camera elevation is exactly 52 degrees; neutral presentation lighting.
world=bpy.data.worlds.new('PreviewWorld'); scene.world=world; world.use_nodes=True
world.node_tree.nodes['Background'].inputs[0].default_value=(.20,.23,.29,1)
world.node_tree.nodes['Background'].inputs[1].default_value=.65
def area(name,location,power,size):
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
    light=bpy.data.objects.new(name,data); scene.collection.objects.link(light); light.location=location
    light.rotation_euler=(Vector((0,0,.4))-light.location).to_track_quat('-Z','Y').to_euler()
area('Key',(-2,2,4),180,3)
area('Fill',(2,-1,2),65,3)
camera_data=bpy.data.cameras.new('PreviewCamera'); camera=bpy.data.objects.new('PreviewCamera',camera_data)
scene.collection.objects.link(camera); scene.camera=camera; camera_data.type='ORTHO'; camera_data.ortho_scale=1.82
def aim(azimuth):
    elev=math.radians(52); az=math.radians(azimuth); distance=3
    target=Vector((0,0,.57))
    camera.location=target+Vector((distance*math.cos(elev)*math.sin(az),distance*math.cos(elev)*math.cos(az),distance*math.sin(elev)))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
scene.render.resolution_x=1200; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
scene.cycles.samples=32; scene.cycles.use_denoising=True
aim(0); scene.render.filepath=str(ART/'Keep_1_front_52.png'); bpy.ops.render.render(write_still=True)
aim(-32); scene.render.filepath=str(ART/'Keep_1_three_quarter_52.png'); bpy.ops.render.render(write_still=True)
aim(0)
bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Keep_1.blend'))
bounds=[obj.matrix_world@Vector(v) for v in obj.bound_box]
report={'triangles':tris,'mesh_objects':1,'materials':len(obj.data.materials),'albedo_size':[1024,1024],
    'dimensions_blender_xyz':list(obj.dimensions),'origin':list(obj.location),'min_z':min(v.z for v in bounds),
    'front_blender':'+Y','front_unity':'-Z','up_unity':'+Y','camera_elevation_degrees':52,
    'metallic':0,'roughness':1,'specular':0,'texture_bake':'EMIT color only; no lighting or specular baked',
    'source':'client/Assets/Resources/Art/Buildings/Keep_1.png','method':'Reference-driven procedural mesh reconstruction',
    'blender':bpy.app.version_string,'external_service_cost':0,'generation_seconds':round(time.monotonic()-START,1)}
(ART/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('KEEP_TRIAL_OK',json.dumps(report))
