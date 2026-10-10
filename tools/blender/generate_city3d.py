"""Reference-driven City3D models. Run --python-exit-code 1 --python this.py -- Keep.
Only named assets are written; Keep_1 and Models/City are never changed.
"""
import sys
sys.dont_write_bytecode=True
import math
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import city3d_common as c
from city3d_common import box,beam,hip,panel,window,diamond,flag,cone,gable,base,seal,banner,hall,shelter,rock


def keep(t):
    c.reset('blue'); base(t)
    h=.84+(t-2)*.15
    box('KeepBody',(0,-.075,.12+h/2),(.42,.39,h))
    for x in (-.20,.20):
        for y in (-.25,.10):
            box('KeepPier',(x,y,.12+h/2),(.045,.045,h),'light')
    for x in (-.12,0,.12): window(x,.127,h-.09,1.1)
    for x in (-.20,.20):
        for y in (-.25,.10):
            for z in (.32,.48,.64,h+.1): box('CornerQuoin',(x,y,z),(.065,.065,.025),'light')
    hip('RoyalRoof',0,-.075,h+.14,.53,.51,.23)
    panel('RoofDormer',0,.16,h+.20,.075,.115,'gold',.035)
    panel('DormerWindow',0,.181,h+.212,.043,.079,'dark')
    panel('DormerLight',0,.186,h+.22,.013,.056,'amber')
    for x in (-.235,.235):
        for y in (-.30,.15): cone('RoofFinial',(x,y,h+.22),.021,.12)
    if t==3:
        hip('UpperCrown',0,-.075,h+.35,.24,.23,.12)
        seal(0,.138,h-.25,.10)
    flag(0,-.075,h+.43 if t==2 else h+.56,.16,.07)
    for x in (-.14,.14): banner(x,.132,h-.35,.21)
    for x in (-.36,.36):
        box('Curtain',(x,-.01,.275),(.06,.72,.34))
        for i in range(7): box('Merlon',(x,-.31+i*.10,.48),(.085,.06,.06),'light')
        # Arcaded galleries are the tier-two signature.
        for y in (-.23,-.08,.07):
            box('GalleryColumn',(x*.83,y,.50),(.033,.033,.22),'gold')
        gable('GalleryRoof',x*.83,-.08,.62,.15,.40,.06)
        for y in (-.32,.32):
            tower=.61 if t==2 else .72
            box('CornerTower',(x,y,.12+tower/2),(.14,.14,tower))
            box('TowerCornice',(x,y,tower+.13),(.17,.17,.035),'light')
            hip('TurretRoof',x,y,tower+.16,.16,.16,.15)
            window(x,y+.075,.35,.85)
            box('TowerBase',(x,y,.15),(.165,.165,.08),'light')
            if y>0: flag(x,y,tower+.34,.10,.05)
            banner(x,y+.079,.48,.15,.045)
            if y<0 or t==3: cone('TowerFinial',(x,y,tower+.345),.015,.085)
    for x in (-.21,.21):
        box('GatePier',(x,.32,.35),(.095,.10,.48))
        for dx in (-.027,.027): box('GateMerlon',(x+dx,.32,.62),(.04,.12,.065),'light')
        banner(x,.38,.26,.25,.065)
    panel('GateFrame',0,.32,.10,.28,.48,'light',.09)
    panel('GateRecess',0,.371,.10,.222,.40,'dark')
    panel('GateOak',0,.378,.103,.188,.35,'wood')
    for x in (-.06,-.03,0,.03,.06): beam('DoorPlank',(x,.388,.11),(x,.388,.38),.0016,'dark',4)
    seal(0,.387,.65,.17)
    for side in (-1,1):
        for i in range(4):
            box('GateJamb',(side*.127,.39,.13+i*.062),(.04,.07,.06),'light')
        beam('DoorChevron',(side*.083,.396,.265),(0,.396,.31),.006,'gold',4)
    for i in range(14):
        rock('Moss',((-1 if i%2 else 1)*.42,-.36+(i//2)*.11,.10),(.025,.03,.012),'moss')
    if t==3:
        for x in (-.28,.28):
            box('UpperWatchTower',(x,-.16,.67),(.10,.10,.42))
            hip('UpperWatchRoof',x,-.16,.90,.14,.14,.14)
            cone('UpperFinial',(x,-.16,1.09),.015,.08)
        for x in (-.36,.36): box('RoyalGoldBand',(x,0,.43),(.08,.71,.017),'gold')


def log_stack(x,y,n=3):
    for i in range(n):
        for j in range(n-i):
            xx=x+(j-(n-i-1)/2)*.045; z=.12+i*.038
            beam('Log',(xx,y-.12,z),(xx,y+.12,z),.021,'wood',8)
            beam('EndGrain',(xx,y+.121,z),(xx,y+.124,z),.016,'grain',8)


def wheel(x,y,z,r=.15,saw=False):
    # Wheel lies in XZ, facing the entrance.
    if saw:
        verts=[]
        for i in range(48):
            a=i*math.tau/48; rr=r*(1 if i%2 else .86)
            verts.append((x+rr*math.cos(a),y,z+rr*math.sin(a)))
        c.mesh('SawBlade',verts,[tuple(range(48))],'iron')
    else:
        for i in range(12):
            a=i*math.tau/12; b=(i+1)*math.tau/12
            beam('WheelRim',(x+r*math.cos(a),y,z+r*math.sin(a)),(x+r*math.cos(b),y,z+r*math.sin(b)),.018,'wood',4)
            if i%2==0: beam('WheelSpoke',(x,y,z),(x+r*math.cos(a),y,z+r*math.sin(a)),.011,'wood',4)
    beam('WheelAxle',(x,y-.025,z),(x,y+.035,z),.024,'gold',8)


def crane(x,y,z=.75):
    box('CraneMast',(x,y,(z+.1)/2),(.048,.048,z-.1),'wood')
    beam('CraneArm',(x-.22,y,z),(x+.07,y,z),.025,'wood',4)
    beam('CraneBrace',(x,y,z-.23),(x-.19,y,z),.014,'wood',4)
    beam('CraneRope',(x-.20,y,z),(x-.20,y,z-.28),.004,'dark',6)
    beam('Hook',(x-.20,y,z-.28),(x-.16,y,z-.30),.007,'iron',6)


def sawmill(t):
    c.reset('red'); base(t)
    hall(-.13,-.15,.41,.39,.40+.10*(t-1),True)
    seal(-.13,.095,.60+.10*(t-1),.12)
    for x in (-.27,.01): window(x,.063,.30+.10*(t-1),.75)
    shelter(.28,-.02,.24,.39,.43+.055*(t-1))
    box('Chimney',(-.29,-.27,.66+.10*(t-1)),(.065,.07,.31))
    box('ChimneyCap',(-.29,-.27,.82+.10*(t-1)),(.082,.09,.025),'gold')
    wheel(.27,.20,.28,.13,True)
    log_stack(-.28,.28,3); log_stack(.30,-.06,2)
    for i in range(5): box('Lumber',( .24,.34,.12+i*.018),(.24,.08,.015),'wood')
    for x in (-.40,.40):
        box('FencePost',(x,.37,.20),(.028,.03,.22),'wood')
        beam('FenceRail',(x,.13,.20),(x,.41,.20),.012,'wood',4)
    if t>=2:
        wheel(.10,.21,.31,.17)
        box('MillRace',(.10,.33,.094),(.15,.23,.008),'water')
        for x in (-.25,-.02): banner(x,.065,.46,.16,.04)
    if t==3:
        crane(.34,-.30,.96)
        box('RoofLantern',(-.13,-.16,.96),(.11,.12,.12),'wood')
        hip('LanternRoof',-.13,-.16,1.025,.15,.16,.13)


def quarry(t):
    c.reset('blue'); base(t)
    c.M['ochre']=c.material('Sandstone','A88755','D3B883')
    for row in range(3):
        y=-.32+row*.19; z=.16+(2-row)*(.09+.025*t)
        for col in range(4):
            x=-.32+col*.21
            box('CutRockLedge',(x,y,z/2+.09),(.20,.22,z),'ochre')
            rock('FracturedRock',(x-.045,y-.035,z+.07),(.12,.09,.05),'ochre')
    for x in (-.08,.05): beam('CartRail',(x,.08,.12),(x,.43,.12),.009,'iron',4)
    for i in range(6): box('RailSleeper',(-.015,.10+i*.062,.106),(.19,.022,.017),'wood')
    box('StoneCart',(-.015,.28,.19),(.17,.18,.07),'wood')
    for x in (-.09,.06):
        for y in (.23,.33): beam('CartWheel',(x-.009,y,.145),(x+.009,y,.145),.025,'iron',8)
    box('CartLoad',(-.015,.28,.245),(.12,.13,.055),'light')
    for i in range(5+t):
        box('FinishedBlock',(.22+(i%2)*.10,.13+(i//2)*.075,.13+(i%2)*.025),(.083,.065,.075),'light')
    crane(-.28,-.24,.68+.15*(t-1))
    box('SuspendedBlock',(-.47,-.24,.37+.15*(t-1)),(.09,.09,.10),'light')
    banner(-.28,-.209,.39+.10*(t-1),.18,.055)
    if t>=2:
        shelter(.27,-.23,.24,.22,.65)
        for i in range(8): box('QuarryStair',(.39,.33-i*.075,.12+i*.035),(.10,.08,.04),'light')
        banner(.27,-.10,.42,.16,.055)
    if t==3:
        box('HoistTower',(-.27,-.25,.72),(.17,.17,.40))
        hip('HoistRoof',-.27,-.25,.93,.23,.23,.15)
        seal(-.27,-.16,.80,.11)
        wheel(.27,-.08,.51,.075,True)


def mushroom(x,y,z,r=.045,h=.09):
    cone('MushroomStem',(x,y,z+h*.43),r*.23,h*.86,'grain',6,r*.16)
    vertices=[(x,y,z+h+r*.60)]
    for radius,zz in [(r*.55,z+h+r*.48),(r,z+h),(r*.84,z+h-r*.12)]:
        for i in range(8):
            a=i*math.tau/8; vertices.append((x+radius*math.cos(a),y+radius*math.sin(a),zz))
    faces=[(0,1+i,1+(i+1)%8) for i in range(8)]
    for ring in range(2):
        start=1+ring*8; faces += [(start+i,start+(i+1)%8,start+8+(i+1)%8,start+8+i) for i in range(8)]
    faces.append(tuple(range(24,16,-1)))
    c.mesh('GoldenMushroom',vertices,faces,'amber')


def farm(t):
    c.reset('green'); base(t)
    if t<3:
        hall(-.16,-.18,.36,.36,.34+.13*(t-1),True)
        seal(-.16,.048,.49+.13*(t-1),.10)
    else:
        cone('ConservatoryBase',(-.13,-.18,.34),.24,.48,'stone',8,.24)
        c.M['glass']=c.material('OpaqueGlass','7EAF88','C6C58F')
        cone('ConservatoryGlazing',(-.13,-.18,.70),.25,.26,'glass',8,.15)
        for i in range(8):
            a=i*math.tau/8
            beam('GlassRib',(-.13+.25*math.cos(a),-.18+.25*math.sin(a),.57),(-.13+.15*math.cos(a),-.18+.15*math.sin(a),.83),.006,'gold',4)
        cone('GreenRoof',(-.13,-.18,.88),.18,.10,'roof',8,0)
        cone('RoofJewel',(-.13,-.18,.995),.025,.13,'teal',6,0)
        panel('ConservatoryDoor',-.13,.064,.10,.16,.25,'wood')
        seal(-.13,.07,.47,.13)
        banner(-.26,.05,.17,.22,.06); banner(0,.05,.17,.22,.06)
    for index,(x,y) in enumerate([(.25,-.17),(.24,.24),(-.22,.27)][:t+1]):
        box('BedSoil',(x,y,.13),(.28,.25,.06),'soil')
        for xx in (x-.145,x+.145): box('BedEdge',(xx,y,.16),(.024,.27,.085),'wood')
        for yy in (y-.13,y+.13): box('BedEdge',(x,yy,.16),(.31,.024,.085),'wood')
        for j,(dx,dy) in enumerate([(0,0),(-.075,-.06),(.072,.06),(-.07,.07),(.065,-.065)]):
            mushroom(x+dx,y+dy,.16,.045 if j==0 else .027,.12 if j==0 else .065)
        for j in range(8):
            rock('BedLeaves',(x+((j%4)-1.5)*.055,y+(-.065 if j<4 else .065),.18),(.026,.029,.018),'leaf')
    if t==1:
        shelter(.25,-.20,.26,.25,.41)
    elif t==2:
        shelter(.28,-.23,.27,.25,.49)
        cone('WaterBarrel',(.03,.20,.19),.065,.16,'wood',10,.065)
        cone('WaterSurface',(.03,.20,.273),.055,.008,'water',10,.055)
    else:
        shelter(.28,-.25,.26,.30,.54)
        for x in (.08,.40): box('Irrigation',(x,.13,.095),(.025,.58,.018),'water')
        for x in (-.4,.4):
            box('GardenPillar',(x,.40,.22),(.055,.055,.27),'light')
            cone('GardenJewel',(x,.40,.40),.025,.10,'teal',6,0)


def crystal(x,y,z,r,h):
    cone('CrystalBody',(x,y,z+h*.34),r,h*.68,'teal',6,r*.84)
    cone('CrystalPoint',(x,y,z+h*.84),r*.84,h*.32,'rune',6,0)


def mine(t):
    c.reset('blue'); base(t)
    c.M['bedrock']=c.material('Bedrock','566274','859099')
    # Irregular stone mountain with a visible timber-framed portal.
    for i in range(10+t*2):
        x=(-.33+(i%4)*.21); y=-.34+(i//4)*.10
        height=.25+(3-i//4)*.07+.04*t
        rock('MineRock',(x,y,.1+height*.42),(.16,.125,height*.62),'bedrock')
    panel('MineOpening',-.06,.245,.10,.29,.43,'dark',.025)
    for x in (-.24,.12):
        box('PortalPost',(x,.27,.29),(.045,.055,.37),'wood')
        box('PortalShoe',(x,.27,.13),(.075,.08,.09),'gold')
    beam('PortalLintel',(-.28,.27,.49),(.16,.27,.49),.025,'wood',4)
    beam('PortalBrace',(-.24,.28,.37),(-.10,.28,.50),.015,'gold',4)
    beam('PortalBrace',(.12,.28,.37),(-.02,.28,.50),.015,'gold',4)
    seal(-.06,.303,.57,.15)
    for x in (-.12,0): beam('MineRail',(x,.12,.105),(x,.48,.105),.009,'gold',4)
    for i in range(7): box('Sleeper',(-.06,.12+i*.058,.099),(.21,.023,.015),'wood')
    box('OreCart',(-.06,.34,.18),(.18,.16,.08),'wood')
    for x in (-.15,.03):
        for y in (.29,.39): beam('CartWheel',(x-.009,y,.135),(x+.009,y,.135),.026,'iron',8)
    for i in range(5): crystal(-.11+(i%3)*.05,.31+(i//3)*.05,.22,.018,.075)
    for x,y,z,r,h in [(-.35,.22,.12,.055,.25),(.32,.27,.12,.075,.38),(-.12,-.24,.53,.06,.25+.10*t),
                       (.41,.21,.12,.04,.22),(-.42,.15,.12,.033,.19)]: crystal(x,y,z,r,h)
    if t>=2:
        crane(.30,-.22,.75+.09*(t-2))
        shelter(.31,.10,.20,.15,.51)
        banner(.30,.19,.29,.17,.06)
        crystal(.015,-.23,.49,.052,.25+.08*t)
    if t==3:
        for x in (-.23,.12): banner(x,.308,.14,.24,.055)
        for x in (-.40,.40):
            box('CrystalPillar',(x,.38,.24),(.06,.06,.26),'light')
            crystal(x,.38,.38,.025,.11)
        beam('HoistGold',(.30,-.22,.87),(.10,-.22,.87),.013,'gold',4)


def crate(x,y,z,s=.10):
    box('Crate',(x,y,z+s/2),(s,s,s),'wood')
    for xx in (x-s*.43,x+s*.43):
        box('CrateStrap',(xx,y,z+s/2),(.009,s+.005,s+.006),'gold')
    beam('CrateBrace',(x-s*.44,y+s*.51,z+.008),(x+s*.44,y+s*.51,z+s-.008),.005,'light',4)


def barrel(x,y,z,s=.055):
    cone('Barrel',(x,y,z+s),s,s*2,'wood',10,s)
    for zz in (z+s*.25,z+s*1.75): cone('BarrelHoop',(x,y,zz),s*1.03,.011,'iron',10,s*1.03)


def warehouse(t):
    c.reset('red'); base(t); c.M['blue']=c.M['red']
    h=.43+.12*(t-1)
    if t<3: hall(-.12,-.14,.43,.40,h,True)
    else:
        box('WarehouseHall',(-.10,-.14,.10+h/2),(.48,.43,h),'plaster')
        hip('WarehouseHip',-.10,-.14,h+.12,.60,.56,.26)
        for x in (-.33,.13):
            for y in (-.34,.07): box('StonePier',(x,y,.10+h/2),(.05,.05,h),'stone')
    panel('GrandDoorFrame',-.12,.09,.10,.24,.34,'light')
    panel('DoubleDoor',-.12,.10,.11,.193,.285,'wood')
    beam('DoorSeam',(-.12,.108,.11),(-.12,.108,.385),.002,'dark',4)
    for z in (.19,.32): box('DoorBand',(-.12,.11,z),(.19,.008,.010),'gold')
    seal(-.12,.12,.49+.06*(t-1),.13)
    for x in (-.29,.05): window(x,.085,.30,.8)
    shelter(.28,-.03,.24,.39,.42+.05*(t-1))
    for i in range(4+t):
        x=.24+(i%2)*.12; y=-.10+(i//2)*.14
        crate(x,y,.10,.09)
    for x,y in [(-.32,.31),(.27,.34)]: barrel(x,y,.10)
    for i in range(t+1): rock('GrainSack',(.39,.10+i*.09,.22),(.045,.04,.07),'grain')
    if t>=2:
        box('RoofVent',(-.12,-.14,h+.37),(.16,.17,.17),'plaster')
        hip('VentRoof',-.12,-.14,h+.46,.21,.22,.12)
        window(-.12,-.045,h+.32,.55)
        crane(.39,-.27,.68+.1*(t-2))
        for x in (-.29,.05): banner(x,.103,.24,.24,.055)
    if t==3:
        shelter(-.36,-.03,.15,.39,.45)
        for i in range(3): crate(-.37,-.15+i*.13,.10,.085)
        for x in (-.4,.4):
            box('LoadingPost',(x,.40,.23),(.035,.035,.28),'wood')
            cone('PostFinial',(x,.40,.405),.025,.06)


def barracks(t):
    c.reset('blue' if t<3 else 'red'); base(t); c.M['blue']=c.M['red']
    h=.47+.1*(t-1)
    box('BarracksHall',(-.12,-.17,.10+h/2),(.40,.39,h))
    hip('CommandRoof',-.12,-.17,.10+h,.49,.48,.23)
    for x in (-.30,.06):
        box('CommandPier',(x,.031,.10+h/2),(.045,.045,h),'light')
        banner(x,.06,.26,.23,.055)
    panel('DoorFrame',-.12,.045,.10,.20,.30,'light')
    panel('Door',-.12,.052,.108,.16,.26,'wood')
    seal(-.12,.069,.48,.12)
    for x in (-.41,.41):
        box('CourtWall',(x,.05,.20),(.055,.77,.21))
        for i in range(7): box('CourtMerlon',(x,-.27+i*.11,.33),(.07,.046,.055),'light')
    for x in (-.30,.30): box('FrontWall',(x,.40,.20),(.24,.05,.21))
    for i in range(t+1):
        x=-.23+i*.19; y=.25
        beam('DummyPost',(x,y,.10),(x,y,.32),.012,'wood',6)
        beam('DummyArms',(x-.06,y,.28),(x+.06,y,.28),.012,'wood',6)
        cone('DummyHead',(x,y,.37),.028,.066,'grain',8,.022)
        beam('TargetRim',(x,y+.02,.26),(x,y+.029,.26),.039,'gold',12)
        beam('TargetFace',(x,y+.031,.26),(x,y+.035,.26),.03,'red',12)
        beam('TargetBoss',(x,y+.036,.26),(x,y+.042,.26),.01,'iron',8)
    for i in range(4+t):
        x=.22+i*.025
        beam('Spear',(x,-.04,.12),(x,-.04,.40),.0035,'wood',4)
        cone('Spearhead',(x,-.04,.43),.010,.06,'iron',4)
    box('WeaponRack',(.28,-.04,.22),(.20,.045,.024),'wood')
    if t>=2:
        shelter(.28,-.19,.20,.25,.43)
        flag(-.12,-.17,h+.40,.14,.065)
        window(-.12,.04,h-.06,.9)
    if t==3:
        for x in (-.40,.40):
            box('GuardTower',(x,.36,.37),(.12,.12,.53))
            hip('GuardRoof',x,.36,.65,.17,.17,.16)
            flag(x,.36,.85,.10,.045)
            banner(x,.425,.36,.20,.05)
        for x in (-.30,.06): cone('GarrisonFinial',(x,.025,h+.20),.021,.10)


def herb_bed(x,y,t):
    box('HerbBed',(x,y,.13),(.20,.20,.07),'light')
    box('HerbSoil',(x,y,.17),(.17,.17,.014),'soil')
    for i in range(8):
        xx=x+((i%3)-1)*.055; yy=y+((i//3)-1)*.05
        cone('HerbLeaves',(xx,yy,.22),.027,.085,'leaf',5,.005)
        cone('HerbFlower',(xx,yy,.265),.014,.022,'rune' if i%2 else 'grain',5,0)


def dome(x,y,z,r,h):
    rings=[(r,0),(r*.91,h*.35),(r*.61,h*.76),(.018,h)]
    verts=[]
    for rr,zz in rings:
        for i in range(12):
            a=i*math.tau/12; verts.append((x+rr*math.cos(a),y+rr*math.sin(a),z+zz))
    faces=[]
    for j in range(3):
        for i in range(12): faces.append((j*12+i,j*12+(i+1)%12,(j+1)*12+(i+1)%12,(j+1)*12+i))
    faces.append(tuple(range(36,48)))
    c.mesh('HealingDome',verts,faces,'roof')
    for i in range(0,12,2):
        for j in range(3): beam('DomeRib',verts[j*12+i],verts[(j+1)*12+i],.006,'gold',4)


def hospital(t):
    c.reset('teal'); base(t); c.M['blue']=c.M['teal']
    h=.55+.09*(t-1)
    box('WhiteTower',(-.10,-.15,.10+h/2),(.34,.34,h),'plaster')
    for x in (-.26,.06):
        for y in (-.31,.01): box('WhitePier',(x,y,.10+h/2),(.033,.033,h),'light')
    for x in (-.18,-.02): window(x,.025,h-.03,1.0)
    if t<3: hip('TealRoof',-.10,-.15,h+.12,.43,.43,.25)
    else: dome(-.10,-.15,h+.12,.25,.30)
    cone('HospitalFinial',(-.10,-.15,h+.45),.025,.13,'gold',8,0)
    box('EntryHall',(-.10,.115,.275),(.34,.25,.35),'plaster')
    gable('EntryRoof',-.10,.115,.46,.40,.31,.15)
    panel('EntryArch',-.10,.25,.10,.17,.27,'light')
    panel('EntryDoor',-.10,.258,.11,.13,.23,'wood')
    seal(-.10,.278,.50,.115)
    for x in (-.24,.04): banner(x,.259,.22,.19,.045)
    box('WardWing',(.25,-.17,.255),(.24,.39,.31),'plaster')
    gable('WardRoof',.25,-.17,.425,.30,.45,.13)
    window(.25,.033,.19,.85)
    for x in (-.33,.31): herb_bed(x,.31,t)
    if t>=2:
        shelter(.27,.13,.23,.20,.41)
        box('RecoveryBed',(.27,.13,.19),(.19,.085,.055),'wood')
        box('Mattress',(.27,.13,.225),(.18,.08,.024),'plaster')
        box('TealBlanket',(.30,.13,.24),(.10,.082,.012),'teal')
        for x in (-.38,.40):
            cone('Planter',(x,.02,.15),.04,.10,'gold',8,.055)
            cone('Cypress',(x,.02,.34),.052,.30,'leaf',7,0)
    if t==3:
        for side in (-1,1):
            x=side*.39
            for y in (-.29,-.10,.09):
                cone('ArcadeColumn',(x,y,.29),.018,.37,'plaster',8,.018)
                box('ColumnCapital',(x,y,.48),(.05,.05,.027),'gold')
            gable('ArcadeRoof',x,-.10,.51,.17,.49,.10)
        cone('FountainBowl',(.30,.31,.25),.055,.07,'gold',10,.075)
        cone('FountainWater',(.30,.31,.29),.065,.008,'water',10,.065)
        for x in (-.26,.06): cone('TowerFinial',(x,.03,h+.20),.019,.10)


def site(t=0):
    c.reset('blue'); base(0)
    box('BareSoil',(0,0,.095),(.83,.83,.012),'soil')
    for side in (-1,1):
        for i in range(7): box('LowFoundation',(side*.43,-.34+i*.11,.13),(.08,.092,.10),'light')
    for y in (-.43,.43):
        for x in (-.33,-.22,.22,.33): box('EdgeStone',(x,y,.13),(.10,.08,.10),'light')
    for x in (-.40,.40):
        for y in (-.39,.39):
            box('CornerBlock',(x,y,.18),(.13,.13,.17))
            box('Stake',(x,y,.32),(.035,.035,.18),'wood')
            cone('StakeCap',(x,y,.43),.027,.045,'gold',4,0)
    for a,b in [((-.4,-.39),(.4,-.39)),((-.4,-.39),(-.4,.39)),((.4,-.39),(.4,.39))]:
        for i in range(8):
            def p(t): return (a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t,.37-.055*math.sin(t*math.pi))
            beam('Rope',p(i/8),p((i+1)/8),.005,'grain',6)
    for i in range(15):
        x=-.28+(i%5)*.13; y=-.28+(i//5)*.19
        box('OldPaver',(x,y,.108),(.09,.07,.017),'light')
    for i in range(7): rock('Rubble',(.25+(i%2)*.075,-.24+(i//2)*.07,.15),(.05,.04,.04),'light')


BUILDERS={'Keep':keep,'Sawmill':sawmill,'Quarry':quarry,'Farm':farm,'CrystalMine':mine,
          'Warehouse':warehouse,'Barracks':barracks,'Hospital':hospital,'Site':site}
HEIGHTS={'Keep':{2:1.42,3:1.60},'Sawmill':{1:.90,2:1.06,3:1.22},'Quarry':{1:.70,2:.87,3:1.08},
         'Farm':{1:.82,2:.96,3:1.15},'CrystalMine':{1:.84,2:1.05,3:1.22},
         'Warehouse':{1:.86,2:1.07,3:1.25},'Barracks':{1:.92,2:1.12,3:1.30},
         'Hospital':{1:.96,2:1.13,3:1.30},'Site':{0:.32}}


def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else list(BUILDERS)
    for family in args:
        for tier in ([0] if family=='Site' else [2,3] if family=='Keep' else [1,2,3]):
            BUILDERS[family](tier)
            c.finish_asset('Site' if family=='Site' else f'{family}_{tier}',HEIGHTS[family][tier])


if __name__=='__main__': main()
