"""Three purely visual world-city enclosures, scaled in map meters."""
import sys, json
sys.dont_write_bytecode = True
from pathlib import Path
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
import generate_world3d as g


def wall(t):
    h = [.34,.44,.52][t-1]
    # 2.16 clear inner courtyard surrounds the existing 2x2 keep.
    for x in (-1.18,1.18):
        g.box('SideCurtain',(x,0,h/2),(.20,2.56,h),'stone')
        g.box('SideCoping',(x,0,h),(.24,2.58,.07),'edge')
        for y in (-.94,-.63,-.31,0,.31,.63,.94):
            g.box('Battlement',(x,y,h+.09),(.24,.14,.16),'stone')
    g.box('RearCurtain',(0,-1.18,h/2),(2.56,.20,h),'stone')
    g.box('RearCoping',(0,-1.18,h),(2.58,.24,.07),'edge')
    for x in (-.96,-.64,-.32,0,.32,.64,.96):
        g.box('RearMerlon',(x,-1.18,h+.09),(.15,.24,.16),'stone')
    for side in (-1,1):
        g.box('FrontCurtain',(side*.86,1.18,h/2),(.76,.20,h),'stone')
        for x in (.56,.82,1.08):
            g.box('FrontMerlon',(side*x,1.18,h+.07),(.15,.24,.15),'edge')
        for y in (-.8,0,.8):
            g.box('Buttress',(side*1.28,y,h*.42),(.13,.18,h*.84),'edge')
    for i in range(4):
        g.box('GateStep',(0,1.26+i*.08,.016*(4-i)),(.80,.16,.032*(4-i)),'edge')
    g.box('RearDrainApron',(0,-1.45,.025),(2.56,.26,.05),'dark')
    if t>=2:
        for x in (-1.18,1.18):
            for y in (-1.18,1.18):
                tower=.63 if t==2 else .78
                g.box('CornerTower',(x,y,tower/2),(.38,.38,tower),'stone')
                g.box('TowerRim',(x,y,tower),(.44,.44,.085),'edge')
                g.box('OpenWatchDeck',(x,y,tower+.049),(.31,.31,.015),'dark')
                for dx in (-.16,.16):
                    for dy in (-.16,.16):
                        g.box('TowerMerlon',(x+dx,y+dy,tower+.12),(.13,.13,.18),'stone')
                g.box('TowerSlit',(x,y+.197,tower*.65),(.047,.015,.15),'dark')
    if t==3:
        for x in (-.48,.48):
            g.box('GatePylon',(x,1.17,.43),(.21,.32,.86),'stone')
            g.box('GateBand',(x,1.17,.71),(.25,.35,.08),'cut')
            g.box('JadeInlay',(x,1.345,.48),(.075,.014,.27),'teal')
        # Open passage: no opaque box across the door opening.
        g.box('GateLintel',(0,1.17,.86),(1.22,.37,.18),'edge')
        g.box('GateCrown',(0,1.17,1.0),(.52,.31,.13),'stone')
        g.box('SealTablet',(0,1.366,.91),(.22,.025,.24),'teal')
        g.rune(0,1.385,.91,.13)
        for x in (-.55,.55): g.cone('GateFinial',(x,1.17,1.035),.065,.20,'cut',4,0)


for tier in (1,2,3):
    name=f'CityWall_{tier}'; g.reset(810+tier)
    g.PALETTE['cut']='D1A34E'
    g.c.M['cut']=g.c.material('gold','D1A34E');g.c.M['cut']['palette_key']='cut'
    wall(tier);g.collect(name);g.prepare(name,'World3D')
    low,high=g.bounds();size=high-low
    tris=len(g.PARTS[name].data.polygons);assert tris<=1500
    g.ART=g.ROOT/'artifacts/building_polish';g.ART.mkdir(parents=True,exist_ok=True)
    g.preview(name)
    report={'name':name,'triangles':tris,'materials':1,'albedo':[512,512],
            'dimensions_unity_xyz':[size.x,size.z,size.y],'clear_courtyard':2.16,
            'origin':[0,0,0],'front':'-Z','up':'Y','camera_elevation':52,'visual_only':True}
    (g.ART/(name+'.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('CITY_WALL_DONE',name,tris,flush=True)
