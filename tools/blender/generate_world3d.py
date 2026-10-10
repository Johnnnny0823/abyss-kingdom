"""Original low-poly world dressing. Run with -- <family> or -- all."""
import sys
sys.dont_write_bytecode=True
import math
import random
import json
import time
from pathlib import Path
import bpy
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
import city3d_common as c

ROOT=Path(__file__).resolve().parents[2]
ART=ROOT/'artifacts/world3d_previews'
MODELS=ROOT/'client/Assets/Resources/Models'
PALETTE={'stone':'70787B','edge':'A0A498','dark':'282422','wood':'654632',
         'cut':'B28B58','leaf':'3D6545','leaflight':'73914E','dry':'AD975C',
         'soil':'51453B','bone':'C2B69C','iron':'39383F','cloth':'69434D',
         'teal':'37BCAE','rune':'B8FFF1','amber':'F6B647','pit':'302A28'}
PROPS={
 'Pine':({'outer':3.0,'middle':.35},2.35),
 'Broadleaf':({'outer':2.5},2.4),
 'DeadTree':({'middle':1.4,'rim':.7},2.15),
 'AbyssTree':({'rim':.45},2.35),
 'Bush':({'outer':3.0,'middle':.7},.48),
 'GrassTuft':({'outer':9.0,'middle':4.0},.35),
 'Flowers':({'outer':2.5},.32),
 'GlowMushroom':({'rim':1.2},.50),
 'Rock':({'outer':1.3,'middle':2.2,'rim':3.0},.36),
 'Boulder':({'outer':.25,'middle':.55,'rim':.8},1.1),
 'RockSpire':({'rim':.7},1.65),
 'BrokenPillar':({'middle':.18,'rim':.30},1.35),
 'RuinArch':({'middle':.06,'rim':.09},1.9),
 'OldStatue':({'middle':.035,'rim':.065},1.65),
 'Stump':({'outer':.35,'middle':.4},.43),
 'FallenLog':({'outer':.28,'middle':.25},.40)}
FAMILIES=list(PROPS)+['Field_Wood','Field_Stone','Field_Food','Field_Crystal','MonsterCamp','Monster','RimRock','RimRuin','AbyssCrystal']
PARTS={}


def box(name,p,s,mat='stone'): return c.box(name,p,s,mat)
def beam(name,a,b,r=.035,mat='wood',n=6): return c.beam(name,a,b,r,mat,n)
def cone(name,p,r,h,mat='stone',n=7,top=0): return c.cone(name,p,r,h,mat,n,top)
def rock(name,p,s,mat='stone'): return c.rock(name,p,s,mat)


def shard(p,h,r=.12,mat='teal',lean=0):
    x,y,z=p
    obj=cone('CrystalShaft',(x,y,z+h*.33),r,h*.66,mat,5,r*.8)
    tip=cone('CrystalTip',(x,y,z+h*.83),r*.8,h*.34,'rune' if mat=='teal' else mat,5,0)
    for o in (obj,tip):
        for v in o.data.vertices: v.co.x+=lean*(v.co.z+o.location.z-z)/h


def rune(x,y,z,s=.12):
    pts=[(x,y,z+s),(x+s*.6,y,z),(x,y,z-s),(x-s*.6,y,z),(x-s*.15,y,z+s*.75)]
    for a,b in zip(pts,pts[1:]):beam('ContractMark',a,b,s*.055,'rune',4)


def branch(points,r=.09,mat='wood'):
    for i,(a,b) in enumerate(zip(points,points[1:])):
        beam('Branch',a,b,r*(1-i/len(points)*.6),mat,5)


def pine(v=0,x=0,y=0,s=1):
    beam('Trunk',(x,y,0),(x+.05*s,y,1.75*s),.11*s)
    for i in range(4):
        z=(.75+i*.39)*s
        cone('NeedleTier',(x+math.sin(i+v)*.04*s,y,z),(.65-i*.11)*s,.89*s,
             'leaflight' if i==3 else 'leaf',7+v, .025*s)
    for a in range(3):
        angle=a*math.tau/3
        beam('Root',(x,y,.17*s),(x+.30*s*math.cos(angle),y+.30*s*math.sin(angle),.035*s),.045*s)


def broadleaf(v):
    branch([(0,0,0),(-.07,0,.8),(.08,.02,1.6)],.16)
    for i in range(4+v):
        a=i*2.399+v; x=.55*math.cos(a); y=.48*math.sin(a); z=1.65+.19*(i%3)
        branch([(0,0,.75),(x*.65,y*.65,1.5),(x,y,z)],.075)
        rock('LeafCrown',(x,y,z),(.66,.57,.53),'leaflight' if i%2 else 'leaf')


def bare_tree(v,abyss=False):
    mat='pit' if abyss else 'wood'
    trunk=[(0,0,0),(.15,0,.65),(-.12,.09,1.25),(.15,.02,1.87),(.38,.08,2.2)]
    if v:trunk=[(-x,y+.10*math.sin(z*2),z) for x,y,z in trunk]
    branch(trunk,.17,mat)
    for i in range(4):
        a=i*2.2+v; start=trunk[1+i//2]
        end=(math.cos(a)*(.75-i*.05),math.sin(a)*.5,1.25+i*.26)
        mid=((start[0]+end[0])*.5,(start[1]+end[1])*.5,end[2]-.26)
        branch([start,mid,end,(end[0]+.10,end[1],end[2]+.23)],.07,mat)
        if abyss:
            shard((end[0],end[1],end[2]),.18,.045)
            beam('Vein',Vector(start)+Vector((0,.11,0)),Vector(mid)+Vector((0,.04,0)),.012,'teal',4)
    for i in range(4):
        a=i*math.pi/2
        branch([(0,0,.35),(.34*math.cos(a),.34*math.sin(a),.10),(.47*math.cos(a),.47*math.sin(a),.02)],.07,mat)


def grass(v,flowers=False):
    for i in range(9+v*3):
        a=i*2.399; r=.11+.14*(i%3)/2; x=r*math.cos(a); y=r*math.sin(a)
        h=.20+.10*((i*3)%5)/4
        if flowers:
            beam('Stem',(x,y,0),(x,y,h),.009,'leaf',4)
            cone('FlowerHeart',(x,y,h),.028,.022,'dry',5,.02)
            for j in range(4):
                b=j*math.pi/2
                px=x+.034*math.cos(b);py=y+.034*math.sin(b)
                c.mesh('Petal',[(px-.033,py,h),(px,py-.028,h),(px+.033,py,h),(px,py+.028,h+.01)],[(0,1,2,3),(3,2,1,0)],'bone' if v==0 else 'cloth')
        else:
            x*=.55;y*=.55
            verts=[(x-.025,y,0),(x+.025,y,0),(x,y+.007,0),(x+.10*math.cos(a),y+.10*math.sin(a),h)]
            c.mesh('GrassBlade',verts,[(0,1,3),(1,2,3),(2,0,3),(2,1,0)],'dry' if v else 'leaflight')


def mushroom(x,y,z,h=.4,glow=True):
    cone('MushroomStem',(x,y,z+h*.32),h*.055,h*.64,'bone',5,h*.048)
    cone('MushroomGills',(x,y,z+h*.64),h*.37,h*.045,'teal' if glow else 'cut',7,h*.36)
    cone('MushroomCap',(x,y,z+h*.79),h*.39,h*.29,'amber' if glow else 'dry',7,h*.10)


def log(x=0,y=0,z=.15,length=1.3,r=.15):
    beam('LogBark',(x-length/2,y,z),(x+length/2,y,z),r,'wood',7)
    for s in (-1,1):
        beam('CutEnd',(x+s*(length/2+.003),y,z),(x+s*(length/2+.01),y,z),r*.87,'cut',7)
        beam('Heartwood',(x+s*(length/2+.011),y,z),(x+s*(length/2+.016),y,z),r*.43,'dry',7)


def pillar(x=0,y=0,h=1.3):
    box('Plinth',(x,y,.08),(.55,.55,.16),'stone')
    cone('Column',(x,y,h*.48),.19,h*.84,'stone',7,.16)
    rock('BrokenCapital',(x+.035,y,h*.87),(.24,.22,.15),'edge')
    for i in range(3):box('MasonryCourse',(x,y,.24+i*.28),(.38,.38,.035),'edge')


def prop(f,v):
    if f=='Pine':pine(v)
    elif f=='Broadleaf':broadleaf(v)
    elif f in ('DeadTree','AbyssTree'):bare_tree(v,f=='AbyssTree')
    elif f=='Bush':
        for i in range(4+v):
            a=i*2.4
            rock('BushLeaves',(.22*math.cos(a),.19*math.sin(a),.20+.07*(i%2)),(.31,.26,.24),'leaflight' if i%2 else 'leaf')
    elif f in ('GrassTuft','Flowers'):grass(v,f=='Flowers')
    elif f=='GlowMushroom':
        for i in range(3+v):mushroom((i%2)*.25-.15,(i//2)*.22-.12,0,.3+i*.07)
    elif f in ('Rock','Boulder'):
        rock('StoneBody',(0,0,.24),(.55,.42,.34),'stone')
        rock('StoneShoulder',(.32,-.05,.14),(.3,.27,.21),'edge')
        if v:rock('SplitStone',(-.32,.12,.12),(.22,.28,.20),'pit')
    elif f=='RockSpire':
        for i in range(2+v):shard((i*.24-.20,0,0),1.3-i*.21,.22,'pit',.18*(-1 if v else 1))
    elif f=='BrokenPillar':
        pillar(h=1.25+.13*v)
        rock('Rubble',(.38,.20,.10),(.20,.17,.14),'edge')
    elif f=='RuinArch':
        pillar(-.57,0,1.20);pillar(.57,0,.87 if v else 1.2)
        for i in range(3 if v else 6):
            a=math.pi-i*math.pi/5
            obj=box('ArchVoussoir',(.56*math.cos(a),0,1.17+.56*math.sin(a)),(.40,.38,.36))
            obj.rotation_euler.y=a-math.pi/2
        box('FallenKeystone',(.25,.35,.13),(.35,.32,.23),'edge')
    elif f=='OldStatue':
        box('StatueBase',(0,0,.10),(.65,.58,.20),'pit')
        cone('AbstractCloak',(0,0,.65),.32,.94,'stone',5,.20)
        # Nonfigurative ancient boundary idol: offset tablets and a hollow diamond crest.
        box('BlankTablet',(.04,.19,.82),(.33,.10,.48),'edge')
        for a,b in [((0,0,1.43),(.23,0,1.20)),((.23,0,1.20),(0,0,1.00)),((0,0,1.00),(-.23,0,1.20))]:
            beam('BrokenCrest',a,b,.085,'stone',5)
        if v:beam('BrokenArm',(-.21,0,.94),(-.38,.05,.71),.09,'stone',5)
    elif f=='Stump':
        cone('Stump',(0,0,.2),.25,.4,'wood',7,.20)
        cone('CutTop',(0,0,.406),.183,.016,'cut',7,.183)
        cone('GrowthRing',(0,0,.417),.11,.009,'dry',7,.11)
        for i in range(4):
            a=i*1.57+v
            beam('Root',(0,0,.15),(.38*math.cos(a),.38*math.sin(a),.015),.07,'wood',5)
    elif f=='FallenLog':
        log(length=1.4+v*.35)
        branch([(.10,0,.18),(.17,.23,.27),(.30,.31,.31)],.045)
        rock('MossPatch',(-.2,.01,.28),(.22,.12,.045),'leaf')


def base():
    cone('GroundPatch',(0,0,.04),1.0,.08,'soil',12,.95)


def field(f,t):
    base()
    if f=='Field_Wood':
        for i in range(t+1):pine(i%2,-.50+(i%2)*.91,-.34+(i//2)*.51,.41+.05*t)
        for i in range(t+1):log(-.14,.34+i*.16,.16,1.05,.11)
        if t==3:log(-.14,.49,.35,.98,.11)
        box('WoodMarker',(.78,.60,.26),(.12,.12,.50),'wood')
    elif f=='Field_Stone':
        for i in range(t+2):
            rock('QuarryOutcrop',(-.53+(i%3)*.46,-.3+(i//3)*.40,.28),(.38,.31,.26+.05*t),'stone')
        for i in range(t*2):box('CutBlocks',(-.62+(i%3)*.39,.50-(i//3)*.24,.16),(.33,.21,.24),'edge')
        if t==3:
            for x in (-.74,.74):beam('HoistPost',(x,-.6,.06),(x,-.6,1.0),.05)
            beam('HoistBeam',(-.8,-.6,1.0),(.8,-.6,1.0),.06)
            beam('LiftRope',(0,-.6,.95),(0,-.6,.48),.016,'cut',5)
    elif f=='Field_Food':
        for row in range(t+1):
            y=-.55+row*.37
            box('CultivationBed',(0,y,.10),(1.40,.27,.08),'wood')
            for i in range(3):mushroom(-.47+i*.45,y,.13,.24+.04*t,False)
        if t==3:
            for x in (-.8,.8):beam('CanopyPost',(x,-.65,0),(x,-.65,.8),.035)
            c.mesh('ShadeCloth',[(-.87,-.77,.81),(.87,-.77,.81),(.87,-.38,.70),(-.87,-.38,.70)],[(0,1,2,3),(3,2,1,0)],'dry')
    elif f=='Field_Crystal':
        for i in range(t*2+1):
            a=i*2.399;r=.18+.13*(i%3)
            x=r*math.cos(a);y=r*math.sin(a)
            rock('CrystalHost',(x,y,.17),(.3,.26,.19),'pit')
            shard((x,y,.15),.65+.12*t-.06*(i%3),.12+.014*t,lean=.09*math.cos(a))
        for i in range(t):shard((-.70+i*.55,.65,.08),.23,.07)


def camp(t):
    base()
    for i in range(t):
        x=-.40+(i%2)*.80;y=-.28+(i//2)*.65;s=.60 if t>1 else .95
        c.mesh('HideTent',[(x-s/2,y-.32,.09),(x+s/2,y-.32,.09),(x+s/2,y+.32,.09),(x-s/2,y+.32,.09),(x,y,.72)],
               [(0,4,3),(3,4,2),(2,4,1)],'cloth')
        for xx in (x-s/2,x+s/2):beam('TentRib',(xx,y-.32,.07),(x,y,.72),.025,'bone',5)
    for i in range(5+t):
        a=i*math.tau/(5+t);x=.25*math.cos(a);y=.50+.20*math.sin(a)
        rock('FireStone',(x,y,.12),(.10,.09,.10),'stone')
    log(0,.50,.16,.40,.05)
    for i in range(3):shard((i*.07-.07,.5,.15),.25+i*.05,.07,'amber',.02)
    for s in (-1,1):
        beam('TotemPost',(s*.77,.15,0),(s*.77,.15,.85+.18*t),.055,'wood',6)
        rock('FacelessTotem',(s*.77,.15,.79+.18*t),(.13,.10,.18),'bone')
        rune(s*.77,.26,.81+.18*t,.066)
    for i in range(t+1):
        x=-.65+i*.30
        beam('DiscardedBone',(x,-.73,.08),(x+.16,-.62,.12),.03,'bone',5)
    if t==3:
        for x in (-.7,0,.7):shard((x,-.67,.02),.57,.09,'bone',.10)


def joint(name,pivot,fn):
    start=len(c.PARTS);fn()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in c.PARTS[start:]:obj.select_set(True)
    bpy.context.view_layer.objects.active=c.PARTS[start];bpy.ops.object.join()
    obj=bpy.context.object;obj.name=name
    bpy.context.scene.cursor.location=pivot;bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    PARTS[name]=obj


def beast_body(t):
    if t==1:
        rock('RoundHaunch',(0,-.04,.43),(.30,.43,.25),'wood')
        for i in range(3):rock('MossScute',(0,-.25+i*.19,.60),(.27,.14,.12),'leaf')
    elif t==2:
        cone('HollowTorso',(0,0,.67),.26,.65,'cloth',6,.19)
        for s in (-1,1):rock('RibPlate',(s*.15,.08,.70),(.10,.17,.27),'pit')
        rune(0,.255,.70,.14)
    else:
        rock('ShellBody',(0,-.07,.77),(.53,.61,.40),'pit')
        for i in range(4):
            y=-.45+i*.23
            rock('CrownScute',(0,y,1.01),(.44,.18,.18),'iron')
            shard(((-1)**i*.18,y,1.10),.24,.07,'stone',(-1)**i*.06)
        for s in (-1,1):
            for y in (-.30,0,.26):rock('BreathingNode',(s*.45,y,.87),(.04,.06,.05),'teal')


def beast_head(t):
    if t==1:
        rock('BluntHead',(0,.43,.47),(.22,.21,.18),'cut')
        for s in (-1,1):
            rock('SplitMuzzle',(s*.067,.59,.42),(.09,.10,.075),'wood')
            shard((s*.14,.39,.59),.15,.055,'leaf',s*.04)
            rock('Eye',(s*.13,.585,.52),(.029,.018,.023),'amber')
    elif t==2:
        # Open geometric crown, no humanoid face: three offset light apertures.
        for a,b in [((-.23,0,1.03),(0,0,1.26)),((0,0,1.26),(.23,0,1.03)),((.23,0,1.03),(.10,0,.90)),((-.23,0,1.03),(-.10,0,.90))]:beam('HollowHeadRim',a,b,.07,'stone',5)
        for x,z in ((-.095,1.06),(.08,1.13),(.045,.972)):rock('Eye',(x,.04,z),(.045,.025,.035),'teal')
    else:
        rock('HeadShield',(0,.54,.91),(.34,.27,.29),'iron')
        for s in (-1,1):
            branch([(s*.24,.46,1.05),(s*.39,.39,1.34),(s*.25,.35,1.5)],.065,'bone')
            for z in (.82,.99):rock('EyeCluster',(s*.16,.768,z),(.044,.03,.025),'rune')
        box('VerticalMaw',(0,.786,.88),(.036,.03,.24),'dark')
        for s in (-1,1):beam('JawProng',(s*.08,.71,.70),(s*.11,.83,.58),.035,'bone',5)


def beast_leg(t,s,front=True):
    if t==2:
        x=s*.16
        beam('RootThigh',(x,0,.47),(s*.20,.04,.23),.085,'cloth',6)
        beam('RootShin',(s*.20,.04,.23),(s*.17,.07,.04),.057,'pit',6)
        for dx in (-.06,.06):beam('RootToe',(s*.17,.07,.05),(s*.17+dx,.22,.02),.023,'bone',4)
    else:
        size=1 if t==1 else 1.6;y=(.23 if front else -.29)*size;x=s*.20*size
        beam('UpperLeg',(x,y,.44*size),(x*1.18,y+.04,.18*size),.065*size,'wood' if t==1 else 'iron',6)
        cone('Foot',(x*1.18,y+.05,.065*size),.073*size,.13*size,'dark',6,.062*size)
        for dx in (-.035,.035):beam('Toe',(x*1.18+dx,y+.08,.025),(x*1.18+dx,y+.16,.02),.017,'bone',4)


def monster(t):
    joint('Body',(0,0,.42 if t==1 else .68),lambda:beast_body(t))
    joint('Head',(0,.28,.46) if t==1 else ((0,0,.93) if t==2 else (0,.37,.91)),lambda:beast_head(t))
    for s,label in ((1,'L'),(-1,'R')):
        if t==2:
            joint('Leg'+label,(s*.16,0,.47),lambda s=s:beast_leg(t,s))
            def arm(s=s):
                branch([(s*.23,0,.87),(s*.40,.04,.61),(s*.43,.12,.38)],.062,'pit')
                for i in range(3):beam('Finger',(s*.43,.12,.39),(s*(.40+i*.03),.22,.28),.018,'bone',4)
            joint('Arm'+label,(s*.23,0,.87),arm)
        else:
            size=1 if t==1 else 1.6
            for front,fl in ((True,'F'),(False,'B')):
                joint('Leg'+fl+label,(s*.20*size,(.23 if front else -.29)*size,.44*size),lambda s=s,front=front:beast_leg(t,s,front))
    def tail():
        if t==1:branch([(0,-.39,.40),(.12,-.60,.36),(.17,-.72,.40)],.04,'wood')
        else:branch([(0,-.53,.76),(.18,-.88,.64),(.26,-1.02,.91)],.075,'pit');shard((.26,-1.02,.90),.14,.06)
    if t!=2:joint('Tail',(0,-.39,.40) if t==1 else (0,-.53,.76),tail)


def chain(a,b,count=6):
    count=max(count,math.ceil((Vector(b)-Vector(a)).length/.10)+1)
    for i in range(count):
        p=Vector(a).lerp(Vector(b),i/(count-1));p.z-=.14*math.sin(i/(count-1)*math.pi)
        bpy.ops.mesh.primitive_torus_add(major_segments=8,minor_segments=3,location=p,major_radius=.07,minor_radius=.012)
        o=bpy.context.object;o.rotation_euler=((i%2)*math.pi/2,0,0);c.finish(o,'ChainLink','iron')


def abyss(f,v):
    if f=='RimRock':
        for i in range(4+v):
            x=(i%3)*.62-.62;y=(i//3)*.53-.15
            rock('FracturedLedge',(x,y,.30),(.64,.55,.38+.07*(i%2)),'pit')
        for i in range(2+v):shard((-.53+i*.43,.12,.25),.85+i*.10,.16,'dark',.24)
    elif f=='RimRuin':
        for x in (-.55,.55):
            rock('AnchorRock',(x,0,.10),(.26,.27,.15),'pit')
            beam('AnchorPost',(x,0,0),(x,0,1.12 if v else .83),.065,'wood',6)
            box('IronCollar',(x,0,.72),(.16,.15,.045),'iron')
        if v:
            chain((-.55,.035,.98),(.55,.035,.98),8)
            beam('BrokenBrace',(-.55,0,.40),(.1,0,.8),.04)
        else:
            for i in range(6):
                plank=box('BrokenDeck',(-.52+i*.20,.15,.13),(.16,.85-.07*(i%3),.07),'wood')
                plank.rotation_euler.z=(i%3-1)*.06
            beam('Railing',(-.55,0,.78),(.21,0,.77),.04)
            chain((.21,0,.75),(.55,0,.65),3)
    else:
        rock('WallHost',(0,0,.17),(.70,.44,.22),'pit')
        for i in range(4+v):
            a=i*2.399;r=.13+.1*(i%2)
            shard((r*math.cos(a),r*math.sin(a),.15),.65+.13*(i%3),.10,lean=.20+.02*i)


def reset(seed):
    c.reset();PARTS.clear();c.M.clear();random.seed(seed)
    for k,value in PALETTE.items():c.M[k]=c.material(k,value);c.M[k]['palette_key']=k


def collect(name):
    bpy.ops.object.select_all(action='DESELECT')
    for o in c.PARTS:o.select_set(True)
    bpy.context.view_layer.objects.active=c.PARTS[0];bpy.ops.object.join()
    o=bpy.context.object;o.name=name
    bpy.context.scene.cursor.location=(0,0,0);bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
    PARTS[name]=o


def bounds():
    bpy.context.view_layer.update()
    pts=[o.matrix_world@v.co for o in PARTS.values() for v in o.data.vertices]
    return Vector([min(p[i] for p in pts) for i in range(3)]),Vector([max(p[i] for p in pts) for i in range(3)])


def prepare(name,folder,height=None,footprint=None):
    low,high=bounds();shift=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z)) if len(PARTS)==1 else Vector((0,0,low.z))
    size=high-low;scale=Vector((1,1,1))
    if height:scale=Vector((height/size.z,)*3)
    if footprint:scale=Vector((footprint/size.x,footprint/size.y,1))
    for obj in PARTS.values():
        if len(PARTS)==1:
            for v in obj.data.vertices:v.co=(v.co-shift)*scale
            obj.location=(0,0,0)
        else:
            obj.location=(obj.location-shift)*scale
            for v in obj.data.vertices:v.co*=scale
        bpy.context.view_layer.objects.active=obj
        bpy.ops.object.select_all(action='DESELECT');obj.select_set(True)
        bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
        mod=obj.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=mod.name)
    # Color-only atlas: opaque, no lighting, shadows, metallic or specular baked in.
    atlas=bpy.data.images.new(name+'_Albedo',width=512,height=512,alpha=False);atlas.colorspace_settings.name='sRGB'
    pixels=[0.,0.,0.,1.]*(512*512);keys=list(PALETTE)
    for slot,key in enumerate(keys):
        value=PALETTE[key];rgba=[int(value[i:i+2],16)/255 for i in (0,2,4)]+[1.]
        row,col=divmod(slot,4)
        for yy in range(row*128,(row+1)*128):
            start=(yy*512+col*128)*4;pixels[start:start+512]=rgba*128
    atlas.pixels[:]=pixels
    out=MODELS/folder;out.mkdir(parents=True,exist_ok=True)
    atlas.filepath_raw=str(out/(name+'_Albedo.png'));atlas.file_format='PNG';atlas.save()
    mat=bpy.data.materials.new(name+'_Matte');mat.use_nodes=True;bsdf=mat.node_tree.nodes.get('Principled BSDF')
    for k,v in [('Metallic',0),('Roughness',1),('Specular IOR Level',0)]:bsdf.inputs[k].default_value=v
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=atlas;mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    for o in PARTS.values():
        while o.data.uv_layers:o.data.uv_layers.remove(o.data.uv_layers[0])
        uv=o.data.uv_layers.new(name='AlbedoUV');uv.active_render=True
        for face in o.data.polygons:
            key=o.data.materials[face.material_index]['palette_key'];row,col=divmod(keys.index(key),4)
            for i,index in enumerate(face.loop_indices):
                u,v=((.3,.3),(.7,.3),(.5,.7))[i%3];uv.data[index].uv=((col+u)/4,(row+v)/4)
        o.data.materials.clear();o.data.materials.append(mat)
        for face in o.data.polygons:face.material_index=0
    root=None
    if len(PARTS)>1:
        root=bpy.data.objects.new(name,None);bpy.context.collection.objects.link(root)
        for o in PARTS.values():o.parent=root
    bpy.ops.object.select_all(action='DESELECT')
    for o in PARTS.values():o.select_set(True)
    if root:root.select_set(True)
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    bpy.ops.export_scene.fbx(filepath=str(out/(name+'.fbx')),use_selection=True,object_types={'MESH','EMPTY'},
        axis_forward='-Z',axis_up='Y',apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True,use_mesh_modifiers=True,add_leaf_bones=False,bake_anim=False,
        path_mode='COPY',embed_textures=True,use_custom_props=True)


def preview(name):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16;scene.cycles.use_denoising=True
    world=bpy.data.worlds.new('Studio');world.use_nodes=True;scene.world=world
    world.node_tree.nodes['Background'].inputs[0].default_value=(.14,.16,.19,1);world.node_tree.nodes['Background'].inputs[1].default_value=.7
    low,high=bounds();size=high-low;target=(low+high)*.5;extent=max(size)
    for nameL,pos,power in [('Key',(-3,4,6),450),('Fill',(4,1,4),240)]:
        data=bpy.data.lights.new(nameL,'AREA');data.energy=power;data.size=4
        o=bpy.data.objects.new(nameL,data);scene.collection.objects.link(o);o.location=target+Vector(pos)
        o.rotation_euler=(target-o.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',data);scene.collection.objects.link(cam);scene.camera=cam
    data.type='ORTHO';data.ortho_scale=extent*1.52
    el=math.radians(52);az=math.radians(-30)
    cam.location=target+Vector((5*math.cos(el)*math.sin(az),5*math.cos(el)*math.cos(az),5*math.sin(el)))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x=640;scene.render.resolution_y=640;scene.render.resolution_percentage=100
    scene.view_settings.view_transform='Standard';scene.view_settings.look='None'
    scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ART/(name+'.png'))
    bpy.ops.render.render(write_still=True)


def generate(f,index):
    start=time.monotonic();reset(sum(map(ord,f))*10+index);ART.mkdir(parents=True,exist_ok=True)
    height=None;footprint=None;density={};zones=[]
    if f in PROPS:
        name=f+'_'+chr(65+index);folder='WorldProps';budget=800
        density,baseheight=PROPS[f];height=baseheight*(1+.13*index);zones=list(density)
        prop(f,index)
        if f=='Pine' and index:pass
    elif f.startswith('Field_'):
        name=f+'_'+str(index);folder='World3D';budget=1500;footprint=2;field(f,index)
        zones=['outer','middle','rim'];
    elif f=='MonsterCamp':
        name=f+'_'+str(index);folder='World3D';budget=1500;footprint=2;camp(index);zones=[['outer','middle','rim'][index-1]]
    elif f=='Monster':
        name=f+'_'+str(index);folder='World3D';budget=1500;height=[.8,1.2,1.6][index-1];monster(index);zones=[['outer','middle','rim'][index-1]]
    else:
        name=f+'_'+chr(65+index);folder='Abyss';budget=1500;abyss(f,index);zones=['rim']
    if not PARTS:collect(name)
    prepare(name,folder,height,footprint)
    tris=sum(len(o.data.polygons) for o in PARTS.values());assert tris<=budget,(name,tris,budget)
    low,high=bounds();size=high-low
    report={'name':name,'family':f,'folder':folder,'zones':zones,'density_per_100_tiles':density,
            'density_semantics':'Total per family, split equally across A/B; not additive per variant',
            'triangles':tris,'triangle_budget':budget,'texture_size':512,'materials':1,
            'dimensions_unity_xyz':[size.x,size.z,size.y],'up':'Y','forward':'-Z','origin':[0,0,0],
            'parts':[{'name':o.name,'joint_unity_xyz':[o.location.x,o.location.z,-o.location.y],
                      'triangles':len(o.data.polygons)} for o in PARTS.values()],
            'palette':list(PALETTE.values()),'camera_elevation':52,'camera_azimuth':-30,
            'albedo':'sRGB color cells, no baked lighting','unity_runtime_verified':False,
            'target_height':height,'footprint':footprint}
    preview(name)
    report['seconds']=round(time.monotonic()-start,2)
    (ART/(name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('WORLD3D_GENERATED',name,tris,flush=True)


if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else ['all']
    for f in FAMILIES if args==['all'] else args:
        assert f in FAMILIES,f
        indices=range(2) if f in PROPS or f in ('RimRuin','AbyssCrystal') else (range(3) if f=='RimRock' else range(1,4))
        for i in indices:generate(f,i)
