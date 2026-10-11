"""Validate exported FBX, including animated vertex masks and socket hierarchy."""
import sys,json,hashlib
from pathlib import Path
import bpy
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'artifacts/building_polish'
records=[]
names=[f'{f}_{t}' for f in ['Keep','Sawmill','Farm','Warehouse','Barracks','Hospital','CrystalMine'] for t in (1,2,3)]
names += [f'CityWall_{t}' for t in (1,2,3)]
if '--' in sys.argv: names=sys.argv[sys.argv.index('--')+1:]
for name in names:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    folder='World3D' if name.startswith('CityWall') else 'City3D'
    path=ROOT/f'client/Assets/Resources/Models/{folder}/{name}.fbx'
    bpy.ops.import_scene.fbx(filepath=str(path),use_custom_normals=True)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    size=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
    mats={m.name for o in meshes for m in o.data.materials}
    assert len(mats)==1,(name,mats)
    textures={node.image for o in meshes for mat in o.data.materials for node in mat.node_tree.nodes if node.type=='TEX_IMAGE' and node.image}
    expected_size=512 if folder=='World3D' else 1024
    assert len(textures)==1 and all(tuple(image.size)==(expected_size,expected_size) for image in textures),(name,'texture size')
    assert tris <= (1500 if folder=='World3D' else 5000),(name,tris)
    assert abs(min(p.z for p in points))<.001,(name,'not grounded')
    if folder=='City3D': assert abs(size[0]-1)<.001 and abs(size[1]-1)<.001,(name,size)
    flags=[]
    for o in meshes:
        if not o.name.startswith('FlagCloth_'):continue
        a=o.data.color_attributes.active_color
        assert a is not None,(name,o.name,'missing mask')
        weights=[v.color[0] for v in a.data]
        assert min(weights)<.03 and max(weights)>.95,(name,o.name,min(weights),max(weights))
        flags.append({'name':o.name,'mask_min':min(weights),'mask_max':max(weights)})
    sockets=[{'name':o.name,'blender_world':list(o.matrix_world.translation)} for o in bpy.context.scene.objects if o.name.startswith('SmokeSocket_')]
    expected=0 if folder=='World3D' or name.startswith(('Warehouse','CrystalMine')) else 1
    assert len(sockets)==expected,(name,len(sockets),expected)
    r={'name':name,'status':'PASS','triangles':tris,'materials':1,'albedo':[expected_size,expected_size],'dimensions_blender_xyz':size,
       'flags':flags,'smoke_sockets':sockets,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
    (ART/(name+'_verified.json')).write_text(json.dumps(r,indent=2),encoding='utf-8');records.append(r)
    print('POLISH_VERIFIED',name,tris,flush=True)
(ROOT/'tools/blender/building_polish_manifest.json').write_text(json.dumps({'all_pass':True,'models':records},indent=2),encoding='utf-8')
