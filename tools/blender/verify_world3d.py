"""Validate delivered FBX meshes, joints, albedo PNGs and write manifests."""
import bpy
import sys
import json
import hashlib
from pathlib import Path
from io_scene_fbx import parse_fbx
ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'artifacts/world3d_previews'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['all']
reports=[json.loads(p.read_text(encoding='utf-8')) for p in ART.glob('*.json') if not p.stem.endswith('_verified')]
reports=[r for r in reports if args==['all'] or r['family'] in args]
assert reports,'No generated models'
for expected in reports:
    name=expected['name'];folder=expected['folder']
    path=ROOT/'client/Assets/Resources/Models'/folder/(name+'.fbx')
    texpath=path.with_name(name+'_Albedo.png')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=True)
    meshes={o.name:o for o in bpy.context.scene.objects if o.type=='MESH'}
    names={p['name'] for p in expected['parts']}
    assert set(meshes)==names,(name,list(meshes))
    if expected['family']=='Monster':
        required={'Body','Head','LegL','LegR','ArmL','ArmR'} if name=='Monster_2' else {'Body','Head','LegFL','LegFR','LegBL','LegBR','Tail'}
        assert names==required
        root=bpy.data.objects[name]
        assert root.type=='EMPTY' and root.location.length<1e-5
    else:
        assert len(meshes)==1 and meshes[name].location.length<1e-5
    mats=set();images=set();pts=[];tris=0;joints=[]
    assert not any(o.type=='ARMATURE' for o in bpy.context.scene.objects)
    for p in expected['parts']:
        o=meshes[p['name']]
        if len(meshes)>1:assert o.parent==root
        assert len(o.data.materials)==1 and len(o.data.uv_layers)==1
        mat=o.data.materials[0];mats.add(mat)
        textures=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
        assert len(textures)==1 and tuple(textures[0].size)==(512,512)
        images.update(textures)
        bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        for k,v in [('Metallic',0),('Roughness',1),('Specular IOR Level',0)]:assert bsdf.inputs[k].default_value==v
        o.data.calc_loop_triangles();count=len(o.data.loop_triangles);tris+=count;assert count==p['triangles']
        pt=o.matrix_world.translation;pivot=[pt.x,pt.z,-pt.y]
        assert max(abs(a-b) for a,b in zip(pivot,p['joint_unity_xyz']))<1e-5,(name,o.name,pivot,p)
        pts.extend(o.matrix_world@v.co for v in o.data.vertices)
        original=o.rotation_euler.copy();before=o.matrix_world.translation.copy()
        o.rotation_euler.x+=.3;bpy.context.view_layer.update()
        assert (o.matrix_world.translation-before).length<1e-6
        o.rotation_euler=original;bpy.context.view_layer.update()
        joints.append({'name':o.name,'joint_unity_xyz':pivot,'triangles':count,'pivot_rotation_check':True})
        for uv in o.data.uv_layers[0].data:assert 0<uv.uv.x<1 and 0<uv.uv.y<1
    assert len(mats)==len(images)==1 and tris<=expected['triangle_budget'] and tris==expected['triangles']
    low=[min(p[i] for p in pts) for i in range(3)];high=[max(p[i] for p in pts) for i in range(3)]
    dims=[high[0]-low[0],high[2]-low[2],high[1]-low[1]]
    assert abs(low[2])<1e-5,(name,low)
    assert max(abs(a-b) for a,b in zip(dims,expected['dimensions_unity_xyz']))<1e-5
    if expected['target_height']:assert abs(dims[1]-expected['target_height'])<1e-5
    if expected['footprint']:assert abs(dims[0]-2)<1e-5 and abs(dims[2]-2)<1e-5
    if len(meshes)==1:assert abs(low[0]+high[0])<1e-5 and abs(low[1]+high[1])<1e-5
    raw,_=parse_fbx.parse(str(path));objects=next(e for e in raw.elems if e.id==b'Objects')
    transforms={}
    for node in objects.elems:
        if node.id!=b'Model':continue
        model=node.props[1].split(b'\x00')[0].decode()
        props=next(e for e in node.elems if e.id==b'Properties70')
        values={e.props[0].decode():list(e.props[4:]) for e in props.elems if e.props[0] in (b'Lcl Translation',b'Lcl Rotation',b'Lcl Scaling')}
        assert max(abs(v) for v in values.get('Lcl Rotation',[0,0,0]))<1e-5
        assert max(abs(v-1) for v in values.get('Lcl Scaling',[1,1,1]))<1e-5
        transforms[model]=values
    for p in expected['parts']:
        assert max(abs(a-b) for a,b in zip(transforms[p['name']].get('Lcl Translation',[0,0,0]),p['joint_unity_xyz']))<1e-5
    atlas=bpy.data.images.load(str(texpath),check_existing=False);assert tuple(atlas.size)==(512,512)
    for slot,value in enumerate(expected['palette']):
        row,col=divmod(slot,4);offset=((row*128+64)*512+col*128+64)*4
        actual=''.join(f'{round(v*255):02X}' for v in atlas.pixels[offset:offset+3]);assert actual==value,(name,slot,actual,value)
    assert (ART/(name+'.png')).is_file()
    result={**expected,'status':'PASS','dimensions_unity_xyz':dims,'parts':joints,
            'fbx_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
            'albedo_sha256':hashlib.sha256(texpath.read_bytes()).hexdigest(),
            'fbx_transforms_verified':True,'palette_verified':True,'unity_runtime_verified':False}
    (ART/(name+'_verified.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('WORLD3D_VERIFIED',name,tris,flush=True)
verified=[json.loads(p.read_text(encoding='utf-8')) for p in ART.glob('*_verified.json')]
for filename,subset in [('worldprops_manifest.json',[r for r in verified if r['folder']=='WorldProps']),
                        ('world3d_manifest.json',verified)]:
    manifest={'base_commit':'c3bfffb','units':'1 meter = 1 map tile','models':sorted(subset,key=lambda r:r['name'])}
    (ROOT/'tools/blender'/filename).write_text(json.dumps(manifest,indent=2),encoding='utf-8')
