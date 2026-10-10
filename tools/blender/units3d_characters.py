"""Reference-led rigid characters using the adopted Shield primitive toolkit."""
import math
from mathutils import Vector

g=None
name=''


def configure(generator, character):
    global g,name
    g=generator; name=character
    g.PALETTE.update({'skin':'DCAE8C','hair':'584334','white':'E0DDD1','iris':'395664'})
    if name in ('Crossbow','Vesper'):
        g.PALETTE.update({'cloth':'65758B','clothdark':'394859','armor':'8D969E','plate':'BCC5CC'})
    if name=='Vesper':
        g.PALETTE.update({'skin':'F0C5AA','hair':'D4D0C3','iris':'4A86A1'})
    if name=='Hold':
        g.PALETTE.update({'cloth':'B88E3C','clothdark':'89632F','hair':'CBCBC5','skin':'C79B7A'})
    if name in ('Rider','Karst'):
        g.PALETTE.update({'cloth':'506B45','clothdark':'304738','armor':'6C6A64','plate':'92908A',
                          'hair':'E0D8C6','iris':'60492F'})
    return 1.3 if name in ('Rider','Karst') else (1.15 if name in ('Hold','Vesper') else 1.)


def face(veteran=False, smiling=False):
    g.loft('Face',[(.685,.058,.065),(.732,.104,.102),(.853,.127,.119),(.922,.098,.09)],'skin')
    for s in (-1,1):
        x=s*.052
        g.plate('Eye',[(x-.03,.824),(x+.025,.824),(x+.023,.802),(x-.024,.802)],.116,.007,'white')
        g.c.box('Iris',(x,.122,.813),(.017,.005,.022),'iris')
        g.c.box('Pupil',(x,.126,.813),(.008,.004,.018),'dark')
        g.c.beam('Brow',(x-s*.025,.123,.839),(x+s*.031,.114,.85),.004 if name=='Vesper' else .008,'leather' if name=='Vesper' else 'hair',4)
        g.loft('Ear',[(.753,.021,.023),(.804,.024,.024)],'skin',s*.126,0)
    if name=='Vesper':
        g.plate('Nose',[(-.009,.798),(.009,.798),(.014,.776),(-.009,.776)],.126,.012,'skin')
    else:
        g.plate('Nose',[(-.016,.811),(.017,.811),(.023,.768),(-.014,.768)],.134,.025,'skin')
    if smiling:
        g.plate('Smile',[(-.038,.759),(.043,.765),(.022,.735),(-.017,.737)],.110,.012,'white')
    else:
        g.c.beam('Mouth',(-.022,.108,.741),(.024,.108,.744),.0035,'leather',4)
    if veteran:
        g.plate('Beard',[(-.117,.786),(-.061,.757),(0,.75),(.061,.757),(.117,.786),(.100,.697),(.041,.651),(-.041,.651),(-.100,.697)],.105,.050,'hair')
        for s in (-1,1):
            g.c.beam('Moustache',(0,.152,.773),(s*.059,.145,.755),.016,'white',5)
            g.c.beam('BeardLock',(s*.075,.135,.730),(s*.032,.13,.676),.014,'white',4)


def hair(veteran=False):
    key='hair' if veteran else 'leather'
    g.loft('HairCap',[(.867,.13,.12),(.936,.12,.11),(.976,.063,.071)],key,y=-.014)
    for i in range(5):
        x=(i-2)*.041
        if veteran:
            g.c.beam('SweptHair',(x,.082,.926),(x-.016,-.074,.963),.022,key,5)
        else:
            g.plate('HairLock',[(x-.035,.947),(x+.028,.961),(x+.021,.868-(i%2)*.025),(x-.011,.892)],.097,.035,key)
    for s in (-1,1):
        g.plate('SideHair',[(s*.105,.921),(s*.143,.911),(s*.14,.772),(s*.10,.807)],-.006,.090,key)
    if not veteran:
        for x,z in ((-.075,.978),(.01,1.008),(.082,.987)):
            g.plate('TousledHair',[(x-.052,.93),(x+.035,.956),(x+.043,z)],-.025,.085,key)


def hood():
    outer=[(-.153,.693),(-.194,.804),(-.17,.934),(-.065,1.0),(.060,1.0),(.169,.925),(.186,.788),(.141,.69)]
    inner=[(-.108,.726),(-.144,.80),(-.12,.91),(-.046,.947),(.048,.947),(.119,.903),(.143,.795),(.104,.725)]
    verts=[(x,.148,z) for x,z in outer]+[(x,.152,z) for x,z in inner]+[(x,-.145,z) for x,z in outer]
    faces=[]
    for i in range(8):
        j=(i+1)%8
        faces += [(i,j,j+8,i+8),(i,i+16,j+16,j)]
    faces.append(tuple(range(16,24)))
    g.c.mesh('HoodShell',verts,faces,'cloth')
    for a,b in zip(inner,inner[1:]+inner[:1]):
        g.c.beam('HoodPiping',(a[0],.158,a[1]),(b[0],.158,b[1]),.005,'white',4)
    if name=='Vesper':
        for x in (-.07,0,.065):
            g.plate('SilverBangs',[(x-.038,.922),(x+.035,.925),(x+.011,.845)],.117,.019,'hair')
        for s in (-1,1):
            g.plate('SilverSideLocks',[(s*.104,.868),(s*.132,.882),(s*.115,.708),(s*.084,.756)],.095,.025,'hair')


def cloak():
    g.plate('CloakBack',[(-.16,.64),(.16,.64),(.22,.23),(.10,.16),(0,.205),(-.18,.18),(-.22,.25)],-.15,.025,'clothdark')
    for s in (-1,1):
        g.c.mesh('ShoulderCape',[(s*.05,.09,.65),(s*.23,.035,.56),(s*.215,-.15,.30),(s*.11,-.18,.21),(s*.13,-.11,.63)],[(0,1,2,3,4),(4,3,2,1,0)],'cloth')
        g.c.beam('CapeEdge',(s*.05,.096,.65),(s*.23,.041,.56),.007,'white',4)
    g.c.diamond('CapeClasp',0,.11,.63,.05,.05,'gold')
    if name=='Vesper':
        for s in (-1,1):
            g.c.diamond('CapeStar',s*.19,.055,.572,.055,.085,'gold')
            g.c.beam('StarRay',(s*.152,.058,.572),(s*.225,.058,.572),.004,'gold',4)


def light_body():
    g.loft('Neck',[(.59,.057,.055),(.714,.057,.055)],'skin')
    if name=='Vesper':
        g.loft('NeckWrap',[(.625,.082,.073),(.683,.075,.07)],'cloth')
    g.loft('Tunic',[(.345,.112,.08),(.45,.109,.08),(.595,.148,.092)],'leather')
    g.plate('ChestArmor',[(-.12,.577),(0,.613),(.12,.577),(.089,.485),(0,.465),(-.09,.485)],.095,.03,'plate')
    g.c.beam('DiagonalHarness',(-.117,.129,.584),(.09,.122,.408),.016,'leather',4)
    g.loft('Belt',[(.367,.123,.093),(.40,.123,.093)],'leather')
    g.c.diamond('Buckle',0,.101,.385,.052,.045,'gold')
    g.plate('TunicFlap',[(-.11,.374),(.11,.374),(.14,.266),(0,.215),(-.13,.273)],.097,.018,'cloth')
    g.c.beam('FlapTrim',(0,.112,.231),(.119,.112,.275),.006,'white',4)
    cloak()
    g.c.beam('Quiver',(-.10,-.17,.43),(-.18,-.19,.73),.059,'leather',8)
    g.c.beam('QuiverRim',(-.176,-.19,.715),(-.186,-.191,.745),.065,'gold',8)
    for i in range(3):
        x=-.204+i*.027
        g.c.beam('BoltShaft',(x,-.19,.66),(x-.025,-.19,.85),.004,'leather',4)
        g.plate('Fletching',[(x-.04,.807),(x-.035,.858),(x-.004,.837),(x-.006,.799)],-.19,.009,'edge')
    g.c.box('BeltPouch',(-.135,.008,.345),(.058,.085,.087),'leather')


def light_leg(s):
    x=s*.087
    g.c.beam('Trousers',(x,0,.35),(x,0,.19),.057,'skin' if name=='Vesper' else 'leather',7)
    g.loft('Boot',[(0,.06,.091),(.035,.068,.097),(.085,.048,.058),(.238,.054,.055)],'leather',x,.014)
    if name=='Vesper':
        g.loft('LegArmor',[(.13,.05,.053),(.24,.062,.066),(.287,.056,.057)],'plate',x,.008)
    g.c.beam('BootCuff',(x,0,.232),(x,0,.247),.063,'gold',7)


def crossbow():
    x=-.065; z=.475
    g.c.box('CrossbowStock',(x,.22,z),(.055,.35,.046),'leather')
    g.c.box('BoltRail',(x,.24,z+.026),(.022,.36,.012),'plate')
    for s in (-1,1):
        points=[(x,.34,z),(x+s*.12,.36,z),(x+s*.245,.29,z+.013)]
        for a,b in zip(points,points[1:]): g.c.beam('BowLimb',a,b,.020,'plate',5)
        g.c.beam('BowTip',points[-1],(x+s*.255,.265,z+.013),.022,'gold',5)
        g.c.beam('LuminousString',(x+s*.255,.265,z+.025),(x,.11,z+.025),.004,'teal',4)
    g.c.beam('LoadedBolt',(x,.08,z+.041),(x,.43,z+.041),.004,'rune',4)
    g.c.diamond('StockRune',x,.397,z,.024,.045,'teal')
    if name=='Vesper':
        for s in (-1,1):
            g.c.beam('OrnateBowRim',(x+s*.03,.358,z+.025),(x+s*.20,.32,z+.025),.006,'gold',4)
            g.c.diamond('BowGem',x+s*.14,.354,z+.009,.025,.048,'teal')


def light_arm(s):
    g.c.beam('UpperSleeve',(s*.168,0,.585),(s*.208,.055,.46),.050,'leather',7)
    g.c.beam('Bracer',(s*.208,.055,.46),(s*.11,.17,.455),.047,'plate',7)
    g.c.beam('Glove',(s*.11,.17,.455),(s*.073,.21,.465),.041,'leather',6)
    if s==-1: crossbow()


def hold_body():
    g.body()
    g.loft('OchreScarf',[(.606,.13,.123),(.65,.134,.11),(.67,.09,.08)],'cloth')
    g.plate('ScarfTail',[(-.14,.633),(-.085,.63),(-.12,.405),(-.188,.443)],.137,.016,'cloth')
    g.c.diamond('BeltGem',0,.14,.399,.031,.047,'teal')
    for s in (-1,1):
        g.c.box('SupplyPouch',(s*.153,.055,.378),(.065,.06,.09),'leather')


def hold_arm(s):
    if s==1:
        g.arm(1)
        g.plate('ShieldCrown',[(.17,.673),(.18,.73),(.30,.76),(.43,.73),(.445,.673)],.249,.029,'gold')
        g.c.diamond('ShieldCrownGem',.305,.27,.708,.044,.06,'teal')
    else:
        g.c.beam('UpperArm',(-.225,0,.60),(-.29,.045,.445),.068,'dark',8)
        g.loft('CommanderPauldron',[(.545,.11,.105),(.62,.12,.112),(.68,.08,.087)],'plate',-.25,0)
        g.loft('CommanderTrim',[(.546,.114,.11),(.57,.12,.114)],'gold',-.25,0)
        g.c.beam('Bracer',(-.285,.045,.50),(-.30,.095,.405),.07,'armor',7)
        g.loft('Glove',[(.365,.059,.065),(.425,.063,.066)],'leather',-.31,.104)
        g.c.beam('AxeHaft',(-.325,.15,.09),(-.325,.15,.47),.022,'leather',8)
        g.plate('AxeBlade',[(-.33,.27),(-.43,.25),(-.47,.16),(-.38,.04),(-.29,.16)],.15,.046,'plate')
        g.c.diamond('AxeSeal',-.382,.177,.173,.04,.068,'gold')
    g.c.diamond('ShoulderBadge',s*.25,.108,.607,.065,.08,'gold')


def mounted_body():
    g.loft('Neck',[(.62,.065,.059),(.714,.065,.059)],'skin')
    g.loft('RiderTunic',[(.36,.125,.095),(.48,.14,.104),(.62,.17,.10)],'cloth')
    for s in (-1,1):
        g.c.beam('CrossStrap',(s*.13,.105,.61),(-s*.10,.113,.39),.022,'leather',4)
    g.loft('Belt',[(.38,.139,.108),(.415,.139,.108)],'leather')
    g.c.diamond('Buckle',0,.123,.396,.066,.05,'gold')
    g.loft('GreenScarf',[(.62,.135,.126),(.672,.11,.102)],'cloth')
    g.plate('ScarfTail',[(-.075,.65),(.038,.65),(-.015,.50),(-.10,.535)],.132,.02,'cloth')


def mounted_arm(s):
    g.c.beam('Sleeve',(s*.22,0,.60),(s*.265,.04,.45),.06,'cloth',7)
    g.loft('ShoulderArmor',[(.56,.09,.086),(.625,.096,.088),(.663,.063,.06)],'plate',s*.235,0)
    g.loft('ShoulderTrim',[(.55,.093,.087),(.57,.093,.087)],'gold',s*.235,0)
    g.c.beam('Gauntlet',(s*.265,.04,.45),(s*.285,.135,.45),.057,'leather',7)
    if s==-1:
        g.c.beam('Lance',(-.32,.14,.30),(-.38,.14,1.04),.014,'leather',6)
        g.loft('LanceTip',[(1.01,.032,.022),(1.14,.002,.002)],'edge',-.385,.14,4)
        g.plate('GreenPennant',[(-.39,1.02),(-.66,.986),(-.60,.92),(-.65,.86),(-.385,.91)],.14,.006,'cloth')
        g.c.diamond('PennantSeal',-.50,.146,.955,.044,.073,'white')
    else:
        g.c.beam('ReinGrip',(.28,.16,.45),(.16,.39,.37),.009,'leather',5)


def mount():
    g.loft('MountBarrel',[(.36,.16,.26),(.49,.23,.32),(.67,.22,.29),(.735,.14,.23)],'armor',y=-.035)
    g.c.beam('Neck',(0,.20,.55),(0,.40,.83),.145,'plate',8)
    g.loft('MountHead',[(.67,.086,.10),(.80,.116,.15),(.91,.08,.09)],'armor',y=.445)
    g.loft('Muzzle',[(.646,.08,.09),(.717,.094,.11)],'plate',y=.548)
    for s in (-1,1):
        g.loft('Ear',[(.863,.036,.037),(.99,.011,.016)],'armor',s*.071,.40,4)
        g.c.box('MountEye',(s*.071,.584,.807),(.026,.015,.018),'amber')
        g.c.box('MountPupil',(s*.071,.594,.807),(.009,.006,.017),'dark')
        g.c.beam('Bridle',(s*.10,.50,.78),(s*.088,.613,.69),.012,'leather',4)
        g.c.beam('Reins',(s*.094,.60,.70),(s*.17,.08,.80),.008,'leather',4)
    g.plate('ForeheadPlate',[(-.064,.86),(0,.894),(.064,.86),(.061,.78),(0,.746),(-.061,.78)],.555,.018,'cloth')
    g.c.diamond('MountCrest',0,.569,.82,.068,.108,'gold')
    g.c.diamond('MountJade',0,.573,.82,.027,.052,'teal')
    for i in range(5):
        y=.05+i*.073; z=.71+i*.032
        g.c.mesh('ManeLock',[(-.075,y,z),(.075,y,z),(0,y-.075,z-.10),(0,y+.03,z+.025)],[(0,1,2),(0,3,1),(0,2,3),(1,3,2)],'hair')
    for s in (-1,1):
        for i in range(4):
            y=.15+i*.068; z=.71+i*.037
            g.c.mesh('SideMane',[(s*.10,y,z+.065),(s*.17,y-.01,z),
                (s*.16,y-.11,z-.075),(s*.08,y-.06,z)],[(0,1,2),(0,2,3),(3,2,1),(3,1,0)],'hair')
    g.c.beam('Tail',(0,-.30,.63),(0,-.44,.37),.06,'hair',6)
    g.c.beam('TailEnd',(0,-.44,.38),(0,-.48,.27),.075,'hair',5)
    g.c.box('Saddle',(0,-.045,.718),(.30,.30,.058),'leather')
    for s in (-1,1):
        obj=g.plate('SaddleCloth',[(-.17,.66),(.13,.66),(.16,.38),(-.14,.40)],0,.012,'cloth')
        # Side-facing plane: the original x coordinate becomes mount fore/aft.
        for v in obj.data.vertices:
            x,y,z=v.co; v.co=(s*.235+y,x-.07,z)
        g.c.beam('ClothTrim',(s*.247,-.20,.413),(s*.247,.065,.393),.009,'gold',4)


def mount_leg(s,front):
    y=.20 if front else -.25
    x=s*.156
    g.c.beam('UpperMountLeg',(x,y,.52),(s*.185,y+.025,.25),.071,'armor',7)
    g.c.beam('LowerMountLeg',(s*.185,y+.025,.25),(s*.18,y+.045,.085),.051,'plate',7)
    g.loft('Hoof',[(0,.073,.085),(.056,.08,.088),(.115,.055,.063)],'dark',s*.18,y+.055,6)
    for dx in (-.032,.028):
        g.c.beam('HoofCrack',(s*.18+dx,y+.148,.018),(s*.18+dx*.55,y+.148,.073),.005,'teal',4)
    g.c.beam('HoofRune',(s*.18-.019,y+.152,.057),(s*.18+.028,y+.152,.029),.003,'rune',4)


def build():
    if name in ('Crossbow','Vesper'):
        g.JOINTS.update({'ArmL':(.168,0,.585),'ArmR':(-.168,0,.585),
                         'LegL':(.087,0,.35),'LegR':(-.087,0,.35)})
        g.joint('Body',light_body)
        g.joint('Head',lambda:(face(),hood()))
        for s,suffix in ((1,'L'),(-1,'R')):
            g.joint('Leg'+suffix,lambda s=s:light_leg(s))
            g.joint('Arm'+suffix,lambda s=s:light_arm(s))
    elif name=='Hold':
        g.joint('Body',hold_body)
        g.joint('Head',lambda:(face(veteran=True),hair(veteran=True)))
        for s,suffix in ((1,'L'),(-1,'R')):
            g.joint('Leg'+suffix,lambda s=s:g.leg(s))
            g.joint('Arm'+suffix,lambda s=s:hold_arm(s))
    else:
        g.joint('Body',mounted_body)
        g.joint('Head',g.head if name=='Rider' else lambda:(face(smiling=True),hair()))
        for s,suffix in ((1,'L'),(-1,'R')):
            g.joint('Leg'+suffix,lambda s=s:g.leg(s))
            g.joint('Arm'+suffix,lambda s=s:mounted_arm(s))
        # Rider seated with legs spread outside the saddle, all six parts remain siblings.
        for key,obj in g.PARTS.items():
            for v in obj.data.vertices: v.co*=.58
            obj.location*=.58; obj.location.z+=.54
            if key.startswith('Leg'):
                s=1 if key=='LegL' else -1
                obj.location.x+=s*.112
                obj.rotation_euler.y=s*math.radians(-12)
                g.bpy.context.view_layer.objects.active=obj
                g.bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
                g.bpy.ops.object.transform_apply(location=False,rotation=True,scale=True)
            g.JOINTS[key]=tuple(obj.location)
        g.JOINTS['Mount']=(0,0,.54)
        g.joint('Mount',mount)
        for s,suffix in ((1,'L'),(-1,'R')):
            for front,label in ((True,'F'),(False,'B')):
                key='MountLeg'+label+suffix
                g.JOINTS[key]=(s*.156,.20 if front else -.25,.52)
                g.joint(key,lambda s=s,front=front:mount_leg(s,front))
    g.bpy.context.view_layer.update()
    # Uniform scale for exact total target height, applied to vertices and pivots.
    points=[o.matrix_world@v.co for o in g.PARTS.values() for v in o.data.vertices]
    height=max(p.z for p in points)
    scale=g.HEIGHT/height
    for key,obj in g.PARTS.items():
        for v in obj.data.vertices: v.co*=scale
        obj.location*=scale
        g.JOINTS[key]=tuple(obj.location)
    g.bpy.context.view_layer.update()
