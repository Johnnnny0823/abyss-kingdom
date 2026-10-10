"""Shield trial: rigid joint parts, one color-only atlas, no armature or packages."""
import sys
sys.dont_write_bytecode = True
import bpy
import math
import json
import time
from pathlib import Path
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import city3d_common as c

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Assets/Resources/Models/Units3D'
ART = ROOT / 'artifacts/units3d_previews'
PALETTE = {'armor':'526779', 'plate':'718493', 'edge':'8C9BA1',
           'dark':'222E38', 'gold':'C5A361', 'goldlight':'DDC18A',
           'leather':'604A34', 'cloth':'8D7547', 'clothdark':'594D39',
           'teal':'37BCAE', 'rune':'B8FFF1', 'amber':'F6B647'}
# Blender coordinates: +Y is forward; FBX/Unity coordinates are (x,z,-y).
JOINTS = {'Body':(0,0,.38), 'Head':(0,0,.68),
          'LegL':(.105,0,.37), 'LegR':(-.105,0,.37),
          'ArmL':(.225,0,.61), 'ArmR':(-.225,0,.61)}
PARTS = {}


def joint(name, build):
    start = len(c.PARTS)
    build()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in c.PARTS[start:]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = c.PARTS[start]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    bpy.context.scene.cursor.location = JOINTS[name]
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    mod = obj.modifiers.new('Triangles','TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj['joint_role'] = name
    PARTS[name] = obj


def loft(name, rings, mat, x=0, y=0, sides=8):
    # Octagonal sections: z, half-width, half-depth. Broad planar armor facets.
    verts = [(x+rx*math.sin((i+.5)*math.tau/sides),
              y+ry*math.cos((i+.5)*math.tau/sides),z)
             for z,rx,ry in rings for i in range(sides)]
    faces = [tuple(reversed(range(sides))),
             tuple(range((len(rings)-1)*sides,len(rings)*sides))]
    for row in range(len(rings)-1):
        for i in range(sides):
            a=row*sides+i; b=row*sides+(i+1)%sides
            faces.append((a,b,b+sides,a+sides))
    return c.mesh(name,verts,faces,mat)


def plate(name, outline, y, depth, mat):
    count=len(outline)
    verts=[(x,y+dy,z) for dy in (-depth/2,depth/2) for x,z in outline]
    faces=[tuple(reversed(range(count))),tuple(range(count,count*2))]
    faces += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
    return c.mesh(name,verts,faces,mat)


def body():
    loft('PaddedTorso',[(.36,.125,.10),(.47,.16,.12),(.63,.19,.12)],'dark')
    loft('Breastplate',[(.405,.125,.11),(.50,.174,.135),(.62,.18,.12)],'armor')
    plate('ChestInset',[(-.13,.58),(0,.62),(.13,.58),(.10,.48),(0,.45),(-.10,.48)],.132,.025,'plate')
    c.beam('ChestTrim',(-.13,.151,.58),(0,.158,.615),.009,'gold',4)
    c.beam('ChestTrim',(0,.158,.615),(.13,.151,.58),.009,'gold',4)
    loft('Collar',[(.625,.105,.10),(.663,.095,.095)],'gold')
    loft('Neck',[(.647,.072,.065),(.71,.074,.065)],'dark')
    loft('Belt',[(.38,.14,.116),(.414,.14,.116)],'leather')
    c.diamond('Buckle',0,.125,.399,.074,.065,'goldlight')
    c.diamond('BuckleInset',0,.127,.399,.035,.033,'dark')
    for side in (-1,1):
        x=side*.082
        outline=[(x-.06,.385),(x+.06,.385),(x+.072,.24),(x,.212),(x-.068,.245)]
        plate('SplitTabard',outline,.105,.023,'cloth')
        c.beam('TabardTrim',(x-side*.046,.122,.368),(x-side*.052,.122,.247),.006,'gold',4)
        loft('HipGuard',[(.31,.071,.088),(.395,.075,.088)],'armor',side*.139,0)
    plate('RearSurcoat',[(-.14,.55),(.14,.55),(.18,.28),(.10,.23),(-.17,.27)],-.14,.018,'clothdark')


def head():
    loft('Helmet',[(.67,.115,.107),(.735,.163,.146),(.88,.171,.154),(.965,.105,.097),(1.0,.015,.015)],'armor')
    loft('HelmetBrowBand',[(.84,.173,.156),(.865,.170,.154)],'gold')
    # Dark visor and brass T read at very small map sizes.
    plate('Visor',[(-.14,.842),(.14,.842),(.118,.748),(0,.718),(-.118,.748)],.145,.018,'dark')
    c.box('Brow', (0,.163,.835),(.258,.024,.025),'goldlight')
    plate('NoseGuard',[(-.014,.85),(.014,.85),(.022,.743),(0,.718),(-.022,.743)],.175,.018,'gold')
    for side in (-1,1):
        x=side*.128
        plate('CheekGuard',[(x-side*.024,.806),(x+side*.025,.822),(x+side*.02,.731),(x-side*.034,.699)],.119,.043,'plate')
        c.beam('CheekRim',(x-side*.034,.145,.699),(x+side*.02,.145,.731),.005,'gold',4)
    c.beam('HelmetCrest',(0,.004,.999),(0,.095,.963),.008,'edge',4)
    c.beam('HelmetCrest',(0,.095,.963),(0,.143,.881),.008,'edge',4)


def leg(side):
    x=side*.105
    c.beam('LegPadding',(x,0,.345),(x,0,.115),.065,'dark',8)
    loft('ThighArmor',[(.255,.068,.064),(.365,.069,.061)],'armor',x,0)
    loft('Greave',[(.076,.067,.065),(.205,.073,.075),(.239,.06,.064)],'plate',x,.01)
    plate('KneeCap',[(x-.067,.236),(x,.269),(x+.067,.236),(x+.052,.192),(x,.177),(x-.052,.192)],.078,.037,'armor')
    c.beam('GreaveRidge',(x,.087,.184),(x,.083,.084),.007,'edge',4)
    loft('Boot',[(0,.075,.109),(.035,.083,.118),(.085,.062,.083)],'dark',x,.035)
    loft('Sabatons',[(.036,.079,.108),(.067,.069,.095)],'armor',x,.039)


def arm(side):
    x=side*.25
    c.beam('Sleeve',(side*.224,0,.60),(side*.29,.05,.44),.063,'dark',8)
    loft('Pauldron',[(.555,.095,.094),(.61,.107,.105),(.669,.070,.078)],'plate',x,0)
    loft('PauldronRim',[(.555,.10,.099),(.574,.103,.10)],'gold',x,0)
    c.beam('Vambrace',(side*.284,.043,.51),(side*.298,.091,.428),.072,'armor',8)
    loft('Gauntlet',[(.388,.064,.062),(.449,.07,.065)],'plate',side*.303,.104)
    if side==1:
        shield()
    else:
        c.beam('SpearShaft',(-.335,.145,.035),(-.335,.145,.843),.015,'leather',8)
        for z in (.39,.42,.45,.77,.80):
            c.cone('SpearBinding',(-.335,.145,z),.021,.014,'gold',8,.021)
        loft('SpearHead',[(.814,.025,.018),(.875,.054,.028),(.997,.004,.003)],'edge',-.335,.145,4)
        c.beam('SpearSpine',(-.335,.162,.83),(-.335,.152,.977),.004,'gold',4)


def shield():
    # Anatomical left (+X), attached to ArmL; tall bevelled tower shield.
    x=.305
    outline=[(x-.157,.678),(x+.157,.678),(x+.147,.285),(x+.092,.198),(x,.145),(x-.092,.198),(x-.147,.285)]
    plate('ShieldBacking',outline,.20,.075,'leather')
    plate('ShieldBrassBorder',outline,.244,.025,'gold')
    inner=[(x+(px-x)*.85,.413+(pz-.413)*.90) for px,pz in outline]
    plate('ShieldFace',inner,.261,.014,'armor')
    for a,b in zip(inner,inner[1:]+inner[:1]):
        c.beam('ShieldInnerRim',(a[0],.274,a[1]),(b[0],.274,b[1]),.005,'goldlight',4)
    c.beam('ShieldGrip',(x-.06,.151,.43),(x+.06,.151,.43),.020,'dark',6)
    # Broken diamond contract mark, jade underlay and pale core.
    points=[(x,.48+.095),(x+.065,.48),(x,.48-.12),(x-.065,.48),(x-.014,.48+.073)]
    for a,b in zip(points,points[1:]):
        c.beam('JadeRune',(a[0],.280,a[1]),(b[0],.280,b[1]),.008,'teal',4)
        c.beam('RuneCore',(a[0],.287,a[1]),(b[0],.287,b[1]),.003,'rune',4)
    c.diamond('RuneHeart',x,.288,.474,.032,.077,'rune')
    for dx in (-.126,.126):
        for z in (.633,.325):
            c.cone('Rivet',(x+dx,.279,z),.009,.009,'goldlight',6,.009).rotation_euler.x=math.pi/2


def atlas_and_export():
    OUT.mkdir(parents=True,exist_ok=True)
    ART.mkdir(parents=True,exist_ok=True)
    atlas=bpy.data.images.new('Shield_Albedo',width=1024,height=1024,alpha=False)
    atlas.colorspace_settings.name='sRGB'
    pixels=[0.0,0.0,0.0,1.0]*(1024*1024)
    keys=list(PALETTE)
    for slot,key in enumerate(keys):
        value=PALETTE[key]
        rgba=[int(value[i:i+2],16)/255 for i in (0,2,4)]+[1.0]
        col,row=slot%4,slot//4
        stripe=rgba*256
        for yy in range(row*256,(row+1)*256):
            offset=(yy*1024+col*256)*4
            pixels[offset:offset+1024]=stripe
    atlas.pixels[:]=pixels
    atlas.filepath_raw=str(OUT/'Shield_Albedo.png'); atlas.file_format='PNG'; atlas.save()
    mat=bpy.data.materials.new('Shield_Matte'); mat.use_nodes=True
    bsdf=mat.node_tree.nodes.get('Principled BSDF')
    for key,value in [('Metallic',0),('Roughness',1),('Specular IOR Level',0)]:
        bsdf.inputs[key].default_value=value
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=atlas
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    root=bpy.data.objects.new('Shield',None); bpy.context.collection.objects.link(root)
    root['front_axis']='-Z'; root['up_axis']='Y'; root['animation']='Rigid parts; no armature'
    for name,obj in PARTS.items():
        while obj.data.uv_layers:
            obj.data.uv_layers.remove(obj.data.uv_layers[0])
        uv=obj.data.uv_layers.new(name='AlbedoUV')
        uv.active_render=True
        for face in obj.data.polygons:
            key=obj.data.materials[face.material_index]['palette_key']
            slot=keys.index(key); col,row=slot%4,slot//4
            for i,index in enumerate(face.loop_indices):
                # Large padded cells keep emissive colors exact under mipmapping.
                u,v=((.3,.3),(.7,.3),(.5,.7))[i%3]
                uv.data[index].uv=((col+u)/4,(row+v)/4)
        obj.data.materials.clear(); obj.data.materials.append(mat)
        for face in obj.data.polygons: face.material_index=0
        obj.parent=root
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for obj in PARTS.values(): obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.context.scene.unit_settings.scale_length=1
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Shield.fbx'),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,
        path_mode='COPY',embed_textures=True,use_custom_props=True)
    return root


def previews():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'; scene.cycles.samples=32; scene.cycles.use_denoising=True
    world=bpy.data.worlds.new('PreviewWorld'); scene.world=world; world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.15,.18,.23,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.7
    for name,pos,power in [('Key',(-2,3,4),230),('Fill',(2,1,2),100),('Rim',(0,-2,3),140)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=3
        obj=bpy.data.objects.new(name,data); scene.collection.objects.link(obj); obj.location=pos
        obj.rotation_euler=(Vector((0,0,.5))-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('PreviewCamera'); camera=bpy.data.objects.new('PreviewCamera',data)
    scene.collection.objects.link(camera); scene.camera=camera; data.type='ORTHO'; data.ortho_scale=1.36
    scene.render.resolution_x=1024; scene.render.resolution_y=1024; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.render.film_transparent=False
    scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
    for suffix,elev,az in [('front',0,0),('52',52,-25)]:
        target=Vector((.025,.035,.51)); el=math.radians(elev); a=math.radians(az)
        camera.location=target+Vector((3*math.cos(el)*math.sin(a),3*math.cos(el)*math.cos(a),3*math.sin(el)))
        camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(ART/f'Shield_{suffix}.png')
        bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(ART/'Shield.blend'))


def main():
    started=time.monotonic()
    c.reset()
    c.M.clear()
    for key,value in PALETTE.items():
        c.M[key]=c.material(key,value); c.M[key]['palette_key']=key
    joint('Body',body); joint('Head',head)
    joint('LegL',lambda:leg(1)); joint('LegR',lambda:leg(-1))
    joint('ArmL',lambda:arm(1)); joint('ArmR',lambda:arm(-1))
    count=sum(len(o.data.polygons) for o in PARTS.values())
    assert count<=3000,count
    atlas_and_export()
    previews()
    report={'name':'Shield','triangles':count,'parts':[], 'root_origin':[0,0,0],
            'up':'Y','forward':'-Z','materials':1,'albedo':[1024,1024],
            'albedo_method':'Unlit sRGB color atlas; no baked lighting or highlights',
            'reserved_colors':{k:PALETTE[k] for k in ('teal','rune','amber')},
            'reference':'client/Assets/Resources/Art/Troops/Shield.png',
            'external_service_cost':0,'seconds':round(time.monotonic()-started,2),
            'unity_runtime_verified':False}
    coords=[]
    for name,obj in PARTS.items():
        x,y,z=JOINTS[name]
        points=[obj.matrix_world@v.co for v in obj.data.vertices]; coords.extend(points)
        dims=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
        report['parts'].append({'name':name,'joint_unity_xyz':[x,z,-y],
                               'triangles':len(obj.data.polygons),'dimensions_unity_xyz':[dims[0],dims[2],dims[1]]})
    low=[min(p[i] for p in coords) for i in range(3)]; high=[max(p[i] for p in coords) for i in range(3)]
    report['dimensions_unity_xyz']=[high[0]-low[0],high[2]-low[2],high[1]-low[1]]
    report['bounds_unity_min']=[low[0],low[2],-high[1]]
    report['bounds_unity_max']=[high[0],high[2],-low[1]]
    (ART/'Shield.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('UNITS3D_GENERATED',json.dumps(report),flush=True)


if __name__=='__main__':
    main()
