"""Modeling primitives derived from keep_1_trial.py; no export on import."""
import bpy
import math
import random
import json
import time
from pathlib import Path
from mathutils import Vector
PARTS = []
ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'client/Assets/Resources/Models/City3D'
PREVIEWS = ROOT / 'artifacts/city3d_previews'


def reset(roof='blue'):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    PARTS.clear(); M.clear(); random.seed(73)
    ra,rb={'blue':('204991','3869B1'),'red':('8E302B','C95439'),
           'green':('315C3F','628050'),'teal':('237F80','46A6A0')}[roof]
    for key,name,a,b,bricks in [
        ('stone','Masonry','777D80','A0A29B',True),('light','CutStone','9F9C89','C4BCA0',False),
        ('wood','Timber','654328','AC7E45',False),('roof','Slate',ra,rb,True),
        ('plaster','Plaster','C7C6B5','E0DDC5',False),('soil','Soil','514330','736344',False)]:
        M[key]=material(name,a,b,bricks)
    for key,value in {'gold':'D1A34E','dark':'272E36','amber':'F6B647','teal':'37BCAE',
                      'rune':'B8FFF1','blue':'31589F','moss':'627D37','iron':'9FA9AE',
                      'water':'387C9C','leaf':'719543','red':'AA343D','grain':'DDBF62'}.items():
        M[key]=material(key,value); M[key]['swatch']=value


def cone(name,pos,radius,height,mat='gold',vertices=8,top=0):
    bpy.ops.mesh.primitive_cone_add(vertices=vertices,radius1=radius,radius2=top,depth=height,location=pos)
    return finish(bpy.context.object,name,mat)


def gable(name,x,y,z,w,d,h,mat='roof'):
    v=[(x-w/2,y-d/2,z),(x+w/2,y-d/2,z),(x+w/2,y+d/2,z),(x-w/2,y+d/2,z),
       (x,y-d/2,z+h),(x,y+d/2,z+h)]
    mesh(name,v,[(0,3,2,1),(0,1,4),(3,5,2),(0,4,5,3),(1,2,5,4)],mat)
    for a,b in [(0,4),(1,4),(3,5),(2,5),(4,5),(0,3),(1,2)]: beam(name+'Trim',v[a],v[b],.008,'gold',4)


def base(tier):
    box('RockFoundation',(0,0,.035),(1,1,.07))
    box('Paving',(0,-.015,.080),(.94,.94,.025),'light')
    for i in range(4): box('EntryStep',(0,.465-i*.023,.018+i*.018),(.25,.060,.036+i*.036),'light')
    for side in (-1,1):
        for y in (-.38,.30): box('FoundationButtress',(side*.445,y,.086),(.08,.13,.10))


def seal(x,y,z,s=.10):
    diamond('GoldSeal',x,y,z,s,s*1.4,'gold')
    diamond('JadeSeal',x,y+.002,z,s*.77,s*1.1,'teal')
    points=[(x,y+.004,z+s*.34),(x+s*.20,y+.004,z),(x,y+.004,z-s*.3),
            (x-s*.19,y+.004,z),(x-s*.02,y+.004,z+s*.23)]
    for a,b in zip(points,points[1:]): beam('ContractRune',a,b,.0025,'rune',4)


def banner(x,y,z,h=.19,w=.055):
    outline=[(-w/2,h),(w/2,h),(w/2,.025),(0,0),(-w/2,.025)]
    mesh('BannerTrim',[(x+a,y,z+b) for a,b in outline],[(4,3,2,1,0)],'gold')
    mesh('BannerCloth',[(x+a*.75,y+.002,z+.009+b*.91) for a,b in outline],[(4,3,2,1,0)],'blue')
    beam('BannerRod',(x-w*.65,y,z+h),(x+w*.65,y,z+h),.004,'gold',6)
    diamond('BannerMark',x,y+.004,z+h*.42,w*.32,w*.52,'gold')


def hall(x,y,w,d,h,timber=False):
    z=.10
    box('Hall',(x,y,z+h/2),(w,d,h),'plaster' if timber else 'stone')
    box('HallBase',(x,y,z+.015),(w+.025,d+.025,.03),'light')
    for xx in (x-w/2,x+w/2):
        for yy in (y-d/2,y+d/2): box('Post',(xx,yy,z+h/2),(.028,.028,h),'wood' if timber else 'light')
    gable('HallRoof',x,y,z+h,w+.09,d+.07,.20)
    if timber:
        for zz in (z+h*.45,z+h): box('CrossTimber',(x,y+d/2+.013,zz),(w,.022,.023),'wood')
        for side in (-1,1): beam('TimberBrace',(x,y+d/2+.02,z+h*.46),(x+side*w/2,y+d/2+.02,z+h),.012,'wood',4)
    panel('DoorFrame',x,y+d/2+.022,z,.14,.24,'light')
    panel('OakDoor',x,y+d/2+.027,z+.008,.105,.208,'wood')
    diamond('DoorLatch',x,y+d/2+.034,z+.095,.014,.024,'gold')


def shelter(x,y,w,d,z):
    for xx in (x-w/2,x+w/2):
        for yy in (y-d/2,y+d/2):
            box('ShelterPost',(xx,yy,(z+.1)/2),(.035,.035,z-.1),'wood')
            box('PostShoe',(xx,yy,.15),(.065,.065,.10))
    gable('ShelterRoof',x,y,z,w+.06,d+.06,.10)


def rock(name,pos,scale,mat='stone'):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1,radius=1,location=pos)
    obj=bpy.context.object; obj.scale=scale; obj.rotation_euler.z=random.random()*6.28
    return finish(obj,name,mat)


def finish_asset(name,height,after_bake=None):
    started=time.monotonic()
    OUT.mkdir(parents=True,exist_ok=True); PREVIEWS.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for part in PARTS: part.select_set(True)
    bpy.context.view_layer.objects.active=PARTS[0]; bpy.ops.object.join()
    obj=bpy.context.object; obj.name=name; obj.data.name=name+'_Mesh'
    bpy.context.scene.cursor.location=(0,0,0); bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    bounds=[Vector(p) for p in obj.bound_box]
    minimum=[min(p[i] for p in bounds) for i in range(3)]; maximum=[max(p[i] for p in bounds) for i in range(3)]
    sx=1/(maximum[0]-minimum[0]); sy=1/(maximum[1]-minimum[1]); sz=height/(maximum[2]-minimum[2])
    for vertex in obj.data.vertices:
        vertex.co.x=(vertex.co.x-(maximum[0]+minimum[0])/2)*sx
        vertex.co.y=(vertex.co.y-(maximum[1]+minimum[1])/2)*sy
        vertex.co.z=(vertex.co.z-minimum[2])*sz
    obj.data.update()
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.006)
    bpy.ops.object.mode_set(mode='OBJECT')
    mod=obj.modifiers.new('Triangles','TRIANGULATE'); bpy.ops.object.modifier_apply(modifier=mod.name)
    triangles=len(obj.data.polygons); assert triangles<=5000,(name,triangles)
    for loop in obj.data.uv_layers.active.data: loop.uv.x*=.93
    atlas=bpy.data.images.new(name+'_Albedo',width=1024,height=1024,alpha=False); atlas.colorspace_settings.name='sRGB'
    for mat in obj.data.materials:
        node=mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image=atlas; mat.node_tree.nodes.active=node
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=4; scene.render.bake.margin=4
    bpy.ops.object.bake(type='EMIT')
    swatches=[mat for mat in obj.data.materials if 'swatch' in mat]
    pixels=list(atlas.pixels[:]); stride=1024//max(1,len(swatches))
    for slot,mat in enumerate(swatches):
        value=mat['swatch']; rgba=tuple(int(value[i:i+2],16)/255 for i in (0,2,4))+(1,)
        for yy in range(slot*stride,(slot+1)*stride):
            for xx in range(960,1024):
                offset=(yy*1024+xx)*4; pixels[offset:offset+4]=rgba
        for face in obj.data.polygons:
            if obj.data.materials[face.material_index]==mat:
                for index in face.loop_indices: obj.data.uv_layers.active.data[index].uv=(992/1024,(slot+.5)*stride/1024)
    atlas.pixels[:]=pixels; atlas.filepath_raw=str(OUT/(name+'_Albedo.png')); atlas.file_format='PNG'; atlas.save()
    final=bpy.data.materials.new(name+'_Matte'); final.use_nodes=True; bsdf=final.node_tree.nodes.get('Principled BSDF')
    for key,value in [('Metallic',0),('Roughness',1),('Specular IOR Level',0)]: bsdf.inputs[key].default_value=value
    tex=final.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=atlas
    final.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    obj.data.materials.clear(); obj.data.materials.append(final)
    for face in obj.data.polygons: face.material_index=0
    obj['front_axis']='-Z in FBX / +Y in Blender'
    scene.unit_settings.system='METRIC'; scene.unit_settings.scale_length=1
    exported = after_bake(obj, minimum, maximum, (sx,sy,sz)) if after_bake else [obj]
    bpy.ops.object.select_all(action='DESELECT')
    for part in exported: part.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,
        path_mode='COPY',embed_textures=True,use_custom_props=True)
    world=bpy.data.worlds.new('PreviewWorld'); scene.world=world; world.use_nodes=True
    world.node_tree.nodes['Background'].inputs[0].default_value=(.20,.23,.29,1)
    world.node_tree.nodes['Background'].inputs[1].default_value=.65
    for lightname,pos,power in [('Key',(-2,2,4),180),('Fill',(2,-1,2),65)]:
        data=bpy.data.lights.new(lightname,'AREA'); data.energy=power; data.shape='DISK'; data.size=3
        light=bpy.data.objects.new(lightname,data); scene.collection.objects.link(light); light.location=pos
        light.rotation_euler=(Vector((0,0,.4))-light.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('PreviewCamera'); camera=bpy.data.objects.new('PreviewCamera',data)
    scene.collection.objects.link(camera); scene.camera=camera; data.type='ORTHO'; data.ortho_scale=max(1.65,height+.5)
    target=Vector((0,0,height*.44)); elev=math.radians(52); az=math.radians(-32)
    camera.location=target+Vector((3*math.cos(elev)*math.sin(az),3*math.cos(elev)*math.cos(az),3*math.sin(elev)))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x=1024; scene.render.resolution_y=1024; scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'; scene.view_settings.view_transform='Standard'; scene.view_settings.look='None'
    scene.cycles.samples=24; scene.cycles.use_denoising=True
    scene.render.filepath=str(PREVIEWS/(name+'.png')); bpy.ops.render.render(write_still=True)
    report={'name':name,'triangles':triangles,'mesh_count':sum(o.type=='MESH' for o in exported),'material_count':1,'albedo':[1024,1024],
            'dimensions_fbx_xyz':[1,height,1],'origin':[0,0,0],'front':'-Z','up':'+Y',
            'camera_elevation':52,'camera_azimuth':-32,'bake':'EMIT color only','metallic':0,'specular':0,
            'reference':f'client/Assets/Resources/Art/Buildings/{name}.png','seconds':round(time.monotonic()-started,2)}
    (PREVIEWS/(name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('CITY3D_DONE',json.dumps(report),flush=True)
    return report
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
        top_abs=nodes.new('ShaderNodeMath'); top_abs.operation='ABSOLUTE'
        links.new(normal.outputs['Z'],top_abs.inputs[0])
        top=nodes.new('ShaderNodeMath'); top.operation='GREATER_THAN'; top.inputs[1].default_value=.8
        links.new(top_abs.outputs[0],top.inputs[0])
        vertical=nodes.new('ShaderNodeMixRGB')
        links.new(top.outputs[0],vertical.inputs[0])
        links.new(sep.outputs['Z'],vertical.inputs[1]); links.new(sep.outputs['Y'],vertical.inputs[2])
        links.new(vertical.outputs[0],coord.inputs['Y'])
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



