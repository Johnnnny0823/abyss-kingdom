"""Five replacement landmarks; reuse the adopted world asset export pipeline."""
import sys
sys.dont_write_bytecode = True
import math
import json
import time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy
import generate_world3d as g
from mathutils import Vector

NAMES = ['Mountain_A', 'Mountain_B', 'Mountain_C', 'PassTower', 'AbyssGate']
PROFILE = [0, .85, 1.75, 2.35, 1.65, .70, 0]


def mountain(index):
    # Identical end profiles form a ridge along local X, not isolated cones.
    peaks = [[2.35, 2.65, 3.35, 2.95, 3.7, 3.15, 2.65, 2.35],
             [2.35, 2.8, 3.0, 3.85, 3.25, 3.5, 2.8, 2.35],
             [2.35, 2.6, 3.6, 3.1, 3.45, 3.0, 2.65, 2.35]][index]
    verts = []
    for i, peak in enumerate(peaks):
        for j in range(7):
            x = -1.5 + i * 3 / 7
            y = -1.5 + j * .5
            h = PROFILE[j] * peak / 2.35
            if i not in (0, 7) and j not in (0, 3, 6):
                y += .09 * math.sin(i * 2.1 + j + index)
                h *= 1 + .08 * math.sin(i + j * 2 + index)
            verts.append((x, y, h))
    faces = []
    for i in range(7):
        for j in range(6):
            a = i*7+j; b = a+7
            faces.extend([(a,b,b+1),(a,b+1,a+1)])
    # End walls and bottom close the mesh; there is no exposed rim on a join.
    faces.extend([tuple(reversed(range(7))), tuple(range(49,56)), (0,49,55,6)])
    obj = g.c.mesh('Ridge', verts, faces, 'stone')
    for key in ('edge','dark','pit','bone'):
        obj.data.materials.append(g.c.M[key])
    for face in obj.data.polygons:
        h = sum(verts[i][2] for i in face.vertices)/len(face.vertices)
        if h > (2.65 if index != 2 else 3.0):
            face.material_index = 4 if index != 2 else 2
        elif 1.2 < h < 1.75:
            face.material_index = 3
        elif h > 2.2:
            face.material_index = 1


def tower():
    g.box('Footing',(0,0,.10),(1.35,1.35,.20),'dark')
    g.box('Plinth',(0,0,.24),(1.18,1.18,.16),'edge')
    g.cone('TowerCore',(0,0,1.15),.56,1.70,'stone',8,.47)
    for x in (-.46,.46):
        for y in (-.46,.46):
            g.box('Buttress',(x,y,.67),(.22,.22,.86),'stone')
            g.box('ButtressCap',(x,y,1.10),(.25,.25,.12),'edge')
    for z,r in ((.40,.58),(1.32,.53),(1.94,.59),(2.08,.62)):
        g.cone('StoneCourse',(0,0,z),r,.10,'edge',8,r)
    g.cone('WatchDeck',(0,0,2.14),.57,.10,'dark',8,.57)
    for i in range(8):
        a=i*math.tau/8
        obj=g.box('Merlon',(.48*math.sin(a),.48*math.cos(a),2.34),(.25,.22,.32),'stone')
        obj.rotation_euler.z=-a
    g.box('FrontRecess',(0,.507,1.04),(.29,.045,.49),'dark')
    g.box('JadeTablet',(0,.536,1.04),(.20,.025,.32),'teal')
    g.rune(0,.558,1.05,.14)
    for x in (-.25,.25):
        g.box('ArrowSlit',(x,.478,1.63),(.048,.04,.23),'dark')
    g.box('RearSlit',(0,-.502,1.52),(.06,.04,.27),'dark')


def arch_segment(a,b,depth=.38):
    # A square beam with its broad face in the XZ plane.
    mid=(Vector(a)+Vector(b))*.5
    obj=g.box('ArchStone',mid,((Vector(b)-Vector(a)).length+.05,depth,.35),'stone')
    obj.rotation_euler.y=-math.atan2(b[2]-a[2],b[0]-a[0])
    return obj


def gate():
    # Raised well and descending stairs keep the asset above ground, so the
    # existing terrain cannot fill a below-ground opening when placed at Y=0.
    g.box('Foundation',(0,0,.07),(3,3,.14),'dark')
    for x in (-1.18,1.18):
        g.box('WellWall',(x,-.05,.43),(.46,2.75,.72),'pit')
        g.box('RimCourse',(x,-.05,.81),(.50,2.75,.14),'stone')
        for y in (-.95,-.20,.55):
            g.box('RimBlock',(x,y,.93),(.43,.55,.12),'edge')
    g.box('RearWellWall',(0,-1.18,.40),(2.40,.45,.67),'pit')
    g.box('BlackDepth',(0,-.48,.16),(1.85,1.0,.04),'dark')
    for i in range(7):
        y=1.23-i*.25; top=.78-i*.085
        g.box('DescendingStep',(0,y,(top+.14)/2),(1.82,.255,top-.14),'stone' if i<4 else 'pit')
        if i<4:g.box('StepNose',(0,y+.10,top+.008),(1.76,.035,.018),'edge')
    for side in (-1,1):
        x=side*.97
        g.box('ArchFoot',(x,-.76,.98),(.61,.69,.28),'edge')
        g.box('ArchPier',(x,-.76,1.62),(.36,.44,1.12),'stone')
        g.box('PierBand',(x,-.76,1.47),(.44,.51,.13),'pit')
        g.box('SealBed',(x,-.515,1.83),(.24,.07,.46),'dark')
        g.rune(x,-.463,1.84,.15)
        g.shard((side*1.25,-1.10,.88),.43,.09,'teal')
    outline=[(-.97,-.76,2.16),(-.63,-.76,2.55),(-.17,-.76,2.79),(.40,-.76,2.61),(.97,-.76,2.16)]
    for a,b in zip(outline,outline[1:]):arch_segment(a,b)
    # Unequal chips and a detached fragment resting on the wall, not floating.
    g.rock('BrokenCrown',(-.20,-.77,2.96),(.20,.24,.12),'pit')
    g.rock('FallenArchFragment',(1.16,.58,1.08),(.23,.24,.17),'stone')
    g.rune(-.17,-.55,2.72,.13)
    for x in (-.65,.65):
        g.box('DepthSeal',(x,-.84,.29),(.045,.02,.18),'teal')


def generate(name):
    started=time.monotonic();g.reset(110+NAMES.index(name))
    # Replace unused vegetation colors for snow and a near-black well floor.
    g.PALETTE['bone']='DFE8E7';g.PALETTE['dark']='20242B'
    for key in ('bone','dark'):
        g.c.M[key]=g.c.material(key,g.PALETTE[key]);g.c.M[key]['palette_key']=key
    if name.startswith('Mountain'):mountain(NAMES.index(name))
    elif name=='PassTower':tower()
    else:gate()
    g.collect(name)
    g.prepare(name,'World3D',height=2.5 if name=='PassTower' else None)
    low,high=g.bounds();size=high-low;obj=g.PARTS[name]
    tris=len(obj.data.polygons);budget=1200 if name.startswith('Mountain') else 1500
    assert tris<=budget
    if name.startswith('Mountain'):assert 3<=size.z<=4 and abs(size.x-3)<1e-5 and abs(size.y-3)<1e-5
    report={'name':name,'family':'Mountain' if name.startswith('Mountain') else name,
            'folder':'World3D','base_commit':'ba92d85','zones':['rim'] if name=='AbyssGate' else ['outer','middle','rim'],
            'density_per_100_tiles':{},'density_semantics':'Landmark placement; no random density',
            'triangles':tris,'triangle_budget':budget,'texture_size':512,'materials':1,
            'dimensions_unity_xyz':[size.x,size.z,size.y],'up':'Y','forward':'-Z','origin':[0,0,0],
            'parts':[{'name':name,'joint_unity_xyz':[0,0,0],'triangles':tris}],
            'palette':list(g.PALETTE.values()),'camera_elevation':52,'camera_azimuth':-30,
            'albedo':'sRGB color cells, no baked lighting','unity_runtime_verified':False,
            'target_height':2.5 if name=='PassTower' else None,
            'footprint':3 if name!='PassTower' else None}
    if name.startswith('Mountain'):
        report['connection']={'axis':'X','straight_center_spacing':3,'curved_suggested_spacing':2.7,
                              'end_profile_blender_yz':[[j*.5-1.5,h] for j,h in enumerate(PROFILE)]}
    g.preview(name);report['seconds']=round(time.monotonic()-started,2)
    (g.ART/(name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('LANDMARK_GENERATED',name,tris,flush=True)


def assembly_preview():
    g.reset(0)
    for index,name in enumerate(NAMES[:3]):
        bpy.ops.import_scene.fbx(filepath=str(g.MODELS/'World3D'/(name+'.fbx')))
        obj=bpy.data.objects[name];obj.location.x=(index-1)*3;g.PARTS[name]=obj
    bpy.context.view_layer.update()
    # Independently verify all six FBX end profiles and each actual seam.
    ends=[]
    for name in NAMES[:3]:
        o=g.PARTS[name]
        for x in (-1.5,1.5):
            points=[o.matrix_world@v.co-o.matrix_world.translation for v in o.data.vertices]
            profile=sorted({(round(p.y,5),round(p.z,5)) for p in points if abs(p.x-x)<1e-5})
            assert profile==[(j*.5-1.5,h) for j,h in enumerate(PROFILE)],(name,x,profile)
            ends.append(profile)
    assert ends[1]==ends[2] and ends[3]==ends[4]
    g.preview('Mountain_Joined_52')
    (g.ROOT/'artifacts/world_landmarks_seams.json').write_text(json.dumps({
        'status':'PASS','axis':'X','spacing':3,'profiles_checked':6,'seams_checked':2,
        'height_gap':0,'preview':'artifacts/world3d_previews/Mountain_Joined_52.png'},indent=2),encoding='utf-8')
    for index,name in enumerate(NAMES[:3]):
        obj=g.PARTS[name];a=(index-1)*math.radians(10);radius=2.7/math.radians(10)
        obj.location=(radius*math.sin(a),radius*(math.cos(a)-1),0)
        obj.rotation_euler.z=-a
    # Remove the previous preview rig before another render.
    for obj in list(bpy.context.scene.objects):
        if obj.type in {'LIGHT','CAMERA'}:bpy.data.objects.remove(obj,do_unlink=True)
    g.preview('Mountain_Curved_52')
    g.reset(0)
    for index in range(2):
        bpy.ops.import_scene.fbx(filepath=str(g.MODELS/'World3D/PassTower.fbx'))
        obj=bpy.context.selected_objects[0];obj.location.x=-1.3 if index==0 else 1.3
        obj.rotation_euler.z=-math.pi/2 if index==0 else math.pi/2
        g.PARTS[str(index)]=obj
    g.preview('PassTower_Pair_52')


if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else NAMES
    g.ART.mkdir(parents=True,exist_ok=True)
    for name in ([] if args==['preview'] else args):
        assert name in NAMES,name
        generate(name)
    if all((g.MODELS/'World3D'/(name+'.fbx')).exists() for name in NAMES[:3]):assembly_preview()
