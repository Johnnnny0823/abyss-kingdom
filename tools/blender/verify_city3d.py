"""Read back generated FBX files and assert deliverable budgets and orientation."""
import bpy
import json
import sys
import hashlib
from pathlib import Path
from io_scene_fbx import parse_fbx

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'client/Assets/Resources/Models/City3D'
ART=ROOT/'artifacts/city3d_previews'
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['all']
paths=sorted({p for family in args for p in OUT.glob('*.fbx' if family=='all' else family+'*.fbx') if p.stem!='Keep_1'})
assert paths,'No FBX files found'
for path in paths:
    root,version=parse_fbx.parse(str(path))
    objects=next(e for e in root.elems if e.id==b'Objects')
    geo=next(e for e in objects.elems if e.id==b'Geometry')
    v=next(e.props[0] for e in geo.elems if e.id==b'Vertices')
    low=[min(v[i::3]) for i in range(3)]; high=[max(v[i::3]) for i in range(3)]
    assert abs(low[1])<.0001,(path.name,low)
    assert abs(high[0]-low[0]-1)<.0001 and abs(high[2]-low[2]-1)<.0001,(path.name,low,high)
    assert abs(low[0]+high[0])<.0001 and abs(low[2]+high[2])<.0001
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=True)
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']; assert len(meshes)==1
    obj=meshes[0]; obj.data.calc_loop_triangles()
    tris=len(obj.data.loop_triangles); assert tris<=5000
    generated=json.loads((ART/(path.stem+'.json')).read_text(encoding='utf-8'))
    assert generated['triangles']==tris
    assert abs(high[1]-generated['dimensions_fbx_xyz'][1])<.0001
    assert obj.location.length<.0001 and len(obj.data.materials)==1
    assert obj.get('front_axis')=='-Z in FBX / +Y in Blender'
    mat=obj.data.materials[0]
    textures=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
    assert len(textures)==1 and tuple(textures[0].size)==(1024,1024)
    bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
    assert bsdf.inputs['Metallic'].default_value==0 and bsdf.inputs['Specular IOR Level'].default_value==0
    assert bsdf.inputs['Roughness'].default_value==1
    assert (ART/(path.stem+'.png')).is_file()
    result={'name':path.stem,'status':'PASS','triangles':tris,'materials':1,'textures':[[1024,1024]],
            'raw_fbx_min_xyz':low,'raw_fbx_max_xyz':high,'origin':list(obj.location),
            'fbx_y_up':True,'front_metadata':'-Z','metallic':0,'specular':0,'roughness':1,
            'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'unity_runtime_verified':False}
    (ART/(path.stem+'_verified.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    print('CITY3D_VERIFIED',json.dumps(result),flush=True)
reports=[json.loads(p.read_text(encoding='utf-8')) for p in sorted(ART.glob('*_verified.json'))]
(ART/'validation_summary.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
