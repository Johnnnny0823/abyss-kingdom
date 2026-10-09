"""Validate the exported FBX independently of the generation scene."""
import bpy
import json
import hashlib
from pathlib import Path
from mathutils import Vector
from io_scene_fbx import parse_fbx

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'client/Assets/Resources/Models/City'
ART=ROOT/'artifacts/keep_1_trial'
path=OUT/'Keep_1.fbx'
root,version=parse_fbx.parse(str(path))
objects=next(e for e in root.elems if e.id==b'Objects')
geometry=next(e for e in objects.elems if e.id==b'Geometry')
vertices=next(e.props[0] for e in geometry.elems if e.id==b'Vertices')
mins=[min(vertices[i::3]) for i in range(3)]
maxs=[max(vertices[i::3]) for i in range(3)]
assert abs(mins[1])<1e-5, ('FBX floor must be Y=0',mins)
assert .99<=maxs[0]-mins[0]<=1.01
assert .98<=maxs[2]-mins[2]<=1.02
assert 1.2<=maxs[1]-mins[1]<=1.4
# Stair lip is the most forward point, raw FBX Z must be negative.
assert abs(mins[2]+.5015)<.0001, ('Entrance axis unexpected',mins,maxs)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(path),use_image_search=True)
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
assert len(meshes)==1
obj=meshes[0]
obj.data.calc_loop_triangles()
assert len(obj.data.loop_triangles)<=5000
assert len(obj.data.materials)==1
assert obj.location.length<1e-5
mat=obj.data.materials[0]
textures=[n.image for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image]
assert textures and all(tuple(im.size)==(1024,1024) for im in textures)
bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
assert bsdf.inputs['Metallic'].default_value==0
assert bsdf.inputs['Specular IOR Level'].default_value==0
bounds=[obj.matrix_world@Vector(v) for v in obj.bound_box]
report={
    'status':'PASS','fbx_version':version,'triangles':len(obj.data.loop_triangles),
    'mesh_count':len(meshes),'material_count':len(obj.data.materials),
    'raw_fbx_y_up_min':mins,'raw_fbx_y_up_max':maxs,
    'entrance':'negative Z confirmed by front stair extent',
    'roundtrip_floor_z':min(v.z for v in bounds),'origin':list(obj.location),
    'texture_sizes':[list(im.size) for im in textures],
    'imported_metallic':bsdf.inputs['Metallic'].default_value,
    'imported_specular':bsdf.inputs['Specular IOR Level'].default_value,
    'imported_roughness':bsdf.inputs['Roughness'].default_value,
    'fbx_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
    'unity_runtime_verified':False,
}
(ART/'fbx_roundtrip_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('KEEP_FBX_VALIDATION',json.dumps(report))
