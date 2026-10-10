"""Validate the delivered Shield FBX by reimport, including each rigid joint."""
import bpy
import json
import math
import hashlib
import sys
from pathlib import Path
from mathutils import Vector
from io_scene_fbx import parse_fbx

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'client/Assets/Resources/Models/Units3D'
ART=ROOT/'artifacts/units3d_previews'
NAME=sys.argv[sys.argv.index('--')+1] if '--' in sys.argv else 'Shield'
path=OUT/(NAME+'.fbx')
expected=json.loads((ART/(NAME+'.json')).read_text(encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=True)
meshes={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
assert set(meshes)=={p['name'] for p in expected['parts']},list(meshes)
required={'Body','Head','LegL','LegR','ArmL','ArmR'}
if NAME in ('Rider','Karst'):
    required.update(('Mount','MountLegFL','MountLegFR','MountLegBL','MountLegBR'))
assert set(meshes)==required
root=bpy.data.objects[NAME]
assert root.type=='EMPTY' and root.location.length<1e-5
assert not any(o.type=='ARMATURE' for o in bpy.context.scene.objects)
materials=set(); images=set(); count=0; parts=[]; coords=[]
for part in expected['parts']:
    obj=meshes[part['name']]
    assert obj.parent==root
    assert len(obj.data.materials)==1
    assert len(obj.data.uv_layers)==1
    for uv in obj.data.uv_layers[0].data:
        assert 0<uv.uv.x<1 and 0<uv.uv.y<1
    mat=obj.data.materials[0]; materials.add(mat)
    textures=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
    assert len(textures)==1 and tuple(textures[0].size)==(1024,1024)
    images.update(textures)
    bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    assert bsdf.inputs['Metallic'].default_value==0
    assert bsdf.inputs['Specular IOR Level'].default_value==0
    assert bsdf.inputs['Roughness'].default_value==1
    obj.data.calc_loop_triangles(); tris=len(obj.data.loop_triangles)
    assert tris==part['triangles']
    count+=tris
    p=obj.matrix_world.translation
    pivot=[p.x,p.z,-p.y]
    assert max(abs(a-b) for a,b in zip(pivot,part['joint_unity_xyz']))<1e-5,(obj.name,pivot)
    points=[obj.matrix_world@v.co for v in obj.data.vertices]; coords.extend(points)
    # Ensure a test rotation pivots around the joint, not the mesh center.
    original=obj.rotation_euler.copy()
    before=obj.matrix_world.translation.copy()
    obj.rotation_euler.x+=math.radians(20)
    bpy.context.view_layer.update()
    assert (obj.matrix_world.translation-before).length<1e-6
    obj.rotation_euler=original; bpy.context.view_layer.update()
    parts.append({'name':obj.name,'joint_unity_xyz':pivot,'triangles':tris,
                  'test_rotation_degrees':20,'pivot_stays_fixed':True})
assert len(materials)==len(images)==1
assert count<=3000 and count==expected['triangles']
low=[min(p[i] for p in coords) for i in range(3)]
high=[max(p[i] for p in coords) for i in range(3)]
dims=[high[0]-low[0],high[2]-low[2],high[1]-low[1]]
target_height={'Shield':1,'Crossbow':1,'Hold':1.15,'Vesper':1.15,'Rider':1.3,'Karst':1.3}[NAME]
assert abs(low[2])<1e-5 and abs(dims[1]-target_height)<.02
assert all(abs(a-b)<1e-5 for a,b in zip(dims,expected['dimensions_unity_xyz']))

# Check actual FBX transforms, not merely a custom forward-axis string.
raw,_=parse_fbx.parse(str(path))
objects=next(e for e in raw.elems if e.id==b'Objects')
raw_transforms={}
for node in objects.elems:
    if node.id!=b'Model': continue
    name=node.props[1].split(b'\x00')[0].decode()
    props=next(e for e in node.elems if e.id==b'Properties70')
    transforms={e.props[0].decode():list(e.props[4:]) for e in props.elems
                if e.props[0] in (b'Lcl Translation',b'Lcl Rotation',b'Lcl Scaling')}
    raw_transforms[name]=transforms
for part in expected['parts']:
    transforms=raw_transforms[part['name']]
    translation=transforms.get('Lcl Translation',[0,0,0])
    assert max(abs(a-b) for a,b in zip(translation,part['joint_unity_xyz']))<1e-5,(part,transforms)
    assert max(abs(v) for v in transforms.get('Lcl Rotation',[0,0,0]))<1e-5,transforms
    assert max(abs(v-1) for v in transforms.get('Lcl Scaling',[1,1,1]))<1e-5
assert all(abs(v)<1e-5 for v in raw_transforms[NAME].get('Lcl Rotation',[0,0,0]))

# Confirm the saved PNG's reserved colors exactly, independent of the scene.
image=bpy.data.images.load(str(OUT/(NAME+'_Albedo.png')),check_existing=False)
palette=expected.get('palette',['526779','718493','8C9BA1','222E38','C5A361','DDC18A',
         '604A34','8D7547','594D39','37BCAE','B8FFF1','F6B647'])
pixel_checks={}
for slot,value in enumerate(palette):
    x=(slot%4)*256+128; y=(slot//4)*256+128; offset=(y*1024+x)*4
    actual=''.join(f'{round(v*255):02X}' for v in image.pixels[offset:offset+3])
    assert actual==value,(slot,actual,value)
    pixel_checks[value]=actual
for suffix in ('front','52'): assert (ART/f'{NAME}_{suffix}.png').is_file()
result={'status':'PASS','name':NAME,'triangles':count,'mesh_count':len(meshes),
        'material_count':len(materials),'texture_count':len(images),'texture_size':[1024,1024],
        'root_origin':[0,0,0],'dimensions_unity_xyz':dims,'parts':parts,
        'raw_fbx_transforms':raw_transforms,'palette_png_checks':pixel_checks,
        'forward_evidence':'Face is authored on Blender +Y / FBX -Z; inspect front preview',
        'fbx_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        'albedo_sha256':hashlib.sha256((OUT/(NAME+'_Albedo.png')).read_bytes()).hexdigest(),
        'unity_runtime_verified':False}
(ART/(NAME+'_verified.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
manifest_path=ROOT/'tools/blender/units3d_manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {
    'base_commit':'18bcc4a','models':[json.loads((ROOT/'tools/blender/shield_manifest.json').read_text(encoding='utf-8'))]}
manifest['models']=[r for r in manifest['models'] if r['name']!=NAME]+[result]
manifest['models'].sort(key=lambda r:r['name'])
manifest_path.write_text(json.dumps(manifest,indent=2),encoding='utf-8')
print('UNITS3D_VERIFIED',json.dumps(result),flush=True)
