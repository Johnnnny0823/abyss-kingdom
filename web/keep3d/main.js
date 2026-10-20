import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import * as BufferGeometryUtils from 'three/addons/utils/BufferGeometryUtils.js';

// ============================================================================
// 1. DETERMINISTIC SEEDED PRNG (Mulberry32)
// ============================================================================
function createSeededRandom(seed = 1337420) {
  let a = seed >>> 0;
  return function () {
    let t = (a += 0x6d2b79f5);
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = createSeededRandom(20260415);

// ============================================================================
// 2. PALETTE & CONSTANTS
// ============================================================================
const PALETTE = {
  slateBlue: 0x2f4fb0,
  slateDeep: 0x1d3582,
  goldTrim: 0xd8a93a,
  goldBright: 0xf4c95d,
  stoneBase: 0x7e8089,
  stoneDark: 0x5c5e67,
  stoneLight: 0x92959f,
  woodDoor: 0x7a4b2a,
  ironStrap: 0x363942,
  runeOuter: 0x37bcae,
  runeCore: 0xb8fff1,
  windowLight: 0xf6b647,
  mossGreen: 0x4e6e22,
  mossBright: 0x6d9331
};

const windUniforms = {
  uTime: { value: 0 }
};

// ============================================================================
// 3. PROCEDURAL CANVAS TEXTURES & MATERIALS
// ============================================================================
function createProceduralMaterials() {
  const texRng = createSeededRandom(9001);

  // --- Stone Ashlar & Weathering Texture (Base #7E8089 with rich block variation) ---
  const stoneCanvas = document.createElement('canvas');
  stoneCanvas.width = 512;
  stoneCanvas.height = 512;
  const sCtx = stoneCanvas.getContext('2d');
  sCtx.fillStyle = '#7E8089';
  sCtx.fillRect(0, 0, 512, 512);

  // Granular stone pores and weathering speckles
  for (let i = 0; i < 12000; i++) {
    const x = texRng() * 512;
    const y = texRng() * 512;
    const v = 85 + Math.floor(texRng() * 60);
    sCtx.fillStyle = `rgba(${v},${v + 2},${v + 7},0.22)`;
    sCtx.fillRect(x, y, 3, 3);
  }

  // Ashlar course lines with stronger per-block tonal variation
  const rows = 16;
  const rowH = 512 / rows;
  for (let r = 0; r < rows; r++) {
    const y = r * rowH;
    sCtx.fillStyle = 'rgba(32,35,42,0.55)';
    sCtx.fillRect(0, y, 512, 2.5);
    sCtx.fillStyle = 'rgba(175,180,192,0.18)';
    sCtx.fillRect(0, y + 2.5, 512, 1.5);

    const cols = 8;
    const colW = 512 / cols;
    const offset = (r % 2) * (colW * 0.5);
    for (let c = 0; c < cols; c++) {
      const x = (c * colW + offset) % 512;
      const shade = Math.floor((texRng() - 0.5) * 42);
      sCtx.fillStyle =
        shade > 0
          ? `rgba(190,196,210,${shade / 115})`
          : `rgba(28,31,38,${-shade / 95})`;
      sCtx.fillRect(x, y + 2, colW - 2, rowH - 2);

      sCtx.fillStyle = 'rgba(32,35,42,0.48)';
      sCtx.fillRect(x, y, 2, rowH);
    }
  }

  const stoneTex = new THREE.CanvasTexture(stoneCanvas);
  stoneTex.wrapS = THREE.RepeatWrapping;
  stoneTex.wrapT = THREE.RepeatWrapping;
  stoneTex.repeat.set(3, 3);
  stoneTex.colorSpace = THREE.SRGBColorSpace;

  const stoneBump = new THREE.CanvasTexture(stoneCanvas);
  stoneBump.wrapS = THREE.RepeatWrapping;
  stoneBump.wrapT = THREE.RepeatWrapping;
  stoneBump.repeat.set(3, 3);

  // --- Blue Roof Slate Texture ---
  const roofCanvas = document.createElement('canvas');
  roofCanvas.width = 256;
  roofCanvas.height = 256;
  const rCtx = roofCanvas.getContext('2d');
  rCtx.fillStyle = '#2A47A1';
  rCtx.fillRect(0, 0, 256, 256);
  for (let r = 0; r < 8; r++) {
    const y = r * 32;
    rCtx.fillStyle = 'rgba(12, 22, 62, 0.55)';
    rCtx.fillRect(0, y, 256, 3);
    for (let c = 0; c < 8; c++) {
      const x = ((c * 32) + (r % 2) * 16) % 256;
      const tint = (texRng() - 0.5) * 32;
      rCtx.fillStyle =
        tint > 0
          ? `rgba(95, 140, 245, ${tint / 120})`
          : `rgba(10, 18, 56, ${-tint / 110})`;
      rCtx.fillRect(x + 1, y + 3, 30, 28);
      rCtx.fillStyle = 'rgba(12, 22, 62, 0.4)';
      rCtx.fillRect(x, y, 2, 32);
    }
  }
  const roofTex = new THREE.CanvasTexture(roofCanvas);
  roofTex.wrapS = THREE.RepeatWrapping;
  roofTex.wrapT = THREE.RepeatWrapping;
  roofTex.colorSpace = THREE.SRGBColorSpace;

  // --- Wood Plank Texture ---
  const woodCanvas = document.createElement('canvas');
  woodCanvas.width = 256;
  woodCanvas.height = 256;
  const wCtx = woodCanvas.getContext('2d');
  wCtx.fillStyle = '#7A4B2A';
  wCtx.fillRect(0, 0, 256, 256);
  for (let i = 0; i < 450; i++) {
    const x = texRng() * 256;
    const y = texRng() * 256;
    wCtx.fillStyle = i % 2 === 0 ? 'rgba(38,19,8,0.22)' : 'rgba(155,98,55,0.16)';
    wCtx.fillRect(x, y, 2, 28 + texRng() * 45);
  }
  const woodTex = new THREE.CanvasTexture(woodCanvas);
  woodTex.wrapS = THREE.RepeatWrapping;
  woodTex.wrapT = THREE.RepeatWrapping;
  woodTex.colorSpace = THREE.SRGBColorSpace;

  // --- Hanging Banner Fabric Texture (Blue with Gold Border & Diamond) ---
  const bannerCanvas = document.createElement('canvas');
  bannerCanvas.width = 256;
  bannerCanvas.height = 512;
  const bCtx = bannerCanvas.getContext('2d');
  const grad = bCtx.createLinearGradient(0, 0, 0, 512);
  grad.addColorStop(0, '#233F9B');
  grad.addColorStop(0.5, '#2F4FB0');
  grad.addColorStop(1, '#1B317C');
  bCtx.fillStyle = grad;
  bCtx.fillRect(0, 0, 256, 512);

  bCtx.strokeStyle = '#D8A93A';
  bCtx.lineWidth = 18;
  bCtx.beginPath();
  bCtx.moveTo(16, 10);
  bCtx.lineTo(240, 10);
  bCtx.lineTo(240, 395);
  bCtx.lineTo(128, 492);
  bCtx.lineTo(16, 395);
  bCtx.closePath();
  bCtx.stroke();

  bCtx.fillStyle = '#E8BE52';
  bCtx.beginPath();
  bCtx.moveTo(128, 250);
  bCtx.lineTo(172, 325);
  bCtx.lineTo(128, 400);
  bCtx.lineTo(84, 325);
  bCtx.closePath();
  bCtx.fill();
  bCtx.strokeStyle = '#9A6D1B';
  bCtx.lineWidth = 4;
  bCtx.stroke();

  const bannerTex = new THREE.CanvasTexture(bannerCanvas);
  bannerTex.colorSpace = THREE.SRGBColorSpace;

  // --- Procedural Ivy Leaf Texture (veined green leaf surface) ---
  const leafCanvas = document.createElement('canvas');
  leafCanvas.width = 128;
  leafCanvas.height = 128;
  const lCtx = leafCanvas.getContext('2d');
  const lGrad = lCtx.createLinearGradient(0, 0, 0, 128);
  lGrad.addColorStop(0, '#6E9430');
  lGrad.addColorStop(0.6, '#557621');
  lGrad.addColorStop(1, '#3D5716');
  lCtx.fillStyle = lGrad;
  lCtx.fillRect(0, 0, 128, 128);
  lCtx.strokeStyle = 'rgba(155, 195, 75, 0.45)';
  lCtx.lineWidth = 3;
  lCtx.beginPath();
  lCtx.moveTo(64, 8);
  lCtx.lineTo(64, 120);
  lCtx.moveTo(64, 42);
  lCtx.lineTo(28, 24);
  lCtx.moveTo(64, 42);
  lCtx.lineTo(100, 24);
  lCtx.moveTo(64, 76);
  lCtx.lineTo(24, 56);
  lCtx.moveTo(64, 76);
  lCtx.lineTo(104, 56);
  lCtx.stroke();

  const leafTex = new THREE.CanvasTexture(leafCanvas);
  leafTex.colorSpace = THREE.SRGBColorSpace;

  // --- Build Shared Materials ---
  const stoneMat = new THREE.MeshStandardMaterial({
    color: 0x92949e,
    map: stoneTex,
    bumpMap: stoneBump,
    bumpScale: 0.07,
    roughness: 0.86,
    metalness: 0.05
  });

  const stoneBlockMat = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    map: stoneTex,
    bumpMap: stoneBump,
    bumpScale: 0.06,
    roughness: 0.84,
    metalness: 0.06
  });

  const darkStoneMat = new THREE.MeshStandardMaterial({
    color: PALETTE.stoneDark,
    map: stoneTex,
    bumpMap: stoneBump,
    bumpScale: 0.06,
    roughness: 0.9,
    metalness: 0.04
  });

  const goldMat = new THREE.MeshStandardMaterial({
    color: PALETTE.goldTrim,
    roughness: 0.3,
    metalness: 0.8
  });

  const roofBaseMat = new THREE.MeshStandardMaterial({
    map: roofTex,
    roughness: 0.55,
    metalness: 0.12
  });

  const roofTileMat = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    map: roofTex,
    roughness: 0.48,
    metalness: 0.15
  });

  const woodMat = new THREE.MeshStandardMaterial({
    map: woodTex,
    roughness: 0.78,
    metalness: 0.08
  });

  const ironMat = new THREE.MeshStandardMaterial({
    color: PALETTE.ironStrap,
    roughness: 0.45,
    metalness: 0.75
  });

  const mossMat = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    map: leafTex,
    roughness: 0.82,
    metalness: 0.02,
    side: THREE.DoubleSide
  });

  const windowGlowMat = new THREE.MeshStandardMaterial({
    color: PALETTE.windowLight,
    emissive: PALETTE.windowLight,
    emissiveIntensity: 2.4,
    roughness: 0.25
  });

  const runeOuterMat = new THREE.MeshStandardMaterial({
    color: PALETTE.runeOuter,
    emissive: PALETTE.runeOuter,
    emissiveIntensity: 2.0,
    roughness: 0.2,
    metalness: 0.1
  });

  const runeCoreMat = new THREE.MeshStandardMaterial({
    color: PALETTE.runeCore,
    emissive: PALETTE.runeCore,
    emissiveIntensity: 3.4,
    roughness: 0.1
  });

  // Vertical Hanging Banner Material with Vertex Wind Shader
  const bannerMat = new THREE.MeshStandardMaterial({
    map: bannerTex,
    roughness: 0.62,
    metalness: 0.12,
    side: THREE.DoubleSide
  });
  bannerMat.onBeforeCompile = (shader) => {
    shader.uniforms.uTime = windUniforms.uTime;
    shader.vertexShader =
      `uniform float uTime;\n` +
      shader.vertexShader.replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
         float pinWeight = clamp(1.0 - uv.y, 0.0, 1.0);
         float wave = sin(uTime * 3.2 - uv.y * 5.5 + position.x * 0.9) * 0.16 * (pinWeight * pinWeight);
         float flutter = cos(uTime * 6.4 - uv.y * 10.0) * 0.035 * pinWeight;
         transformed.z += wave + flutter;
         transformed.x += sin(uTime * 2.1 + uv.y * 4.0) * 0.035 * pinWeight;`
      );
  };

  // Horizontal Swallow-tail Pennant Material with Vertex Wind Shader
  const pennantMat = new THREE.MeshStandardMaterial({
    color: PALETTE.slateBlue,
    roughness: 0.55,
    metalness: 0.15,
    side: THREE.DoubleSide
  });
  pennantMat.onBeforeCompile = (shader) => {
    shader.uniforms.uTime = windUniforms.uTime;
    shader.vertexShader =
      `uniform float uTime;\n` +
      shader.vertexShader.replace(
        '#include <begin_vertex>',
        `#include <begin_vertex>
         float freeFactor = clamp(uv.x, 0.0, 1.0);
         float waveZ = sin(uTime * 4.2 - uv.x * 6.5) * 0.30 * pow(freeFactor, 1.35);
         float waveY = cos(uTime * 3.0 - uv.x * 4.5) * 0.09 * freeFactor;
         transformed.z += waveZ;
         transformed.y += waveY;`
      );
  };

  return {
    stoneMat,
    stoneBlockMat,
    darkStoneMat,
    goldMat,
    roofBaseMat,
    roofTileMat,
    woodMat,
    ironMat,
    mossMat,
    windowGlowMat,
    runeOuterMat,
    runeCoreMat,
    bannerMat,
    pennantMat
  };
}

// ============================================================================
// 4. GEOMETRY COLLECTOR & INSTANCING REGISTRY
// ============================================================================
class KeepBuilderContext {
  constructor(materials) {
    this.materials = materials;
    this.buckets = {
      1: new Map(),
      2: new Map(),
      3: new Map()
    };
    this.instances = {
      1: { stoneBlocks: [], roofSlates: [], mossLeaves: [] },
      2: { stoneBlocks: [], roofSlates: [], mossLeaves: [] },
      3: { stoneBlocks: [], roofSlates: [], mossLeaves: [] }
    };
    this.pointLights = [];
  }

  addGeo(geo, material, tier = 1, matrix = null) {
    const g = matrix ? geo.clone().applyMatrix4(matrix) : geo.clone();
    if (!g.attributes.uv) {
      const count = g.attributes.position.count;
      g.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(count * 2), 2));
    }
    const nonIndexed = g.index ? g.toNonIndexed() : g;
    for (const key of Object.keys(nonIndexed.attributes)) {
      if (key !== 'position' && key !== 'normal' && key !== 'uv') {
        nonIndexed.deleteAttribute(key);
      }
    }
    const map = this.buckets[tier];
    if (!map.has(material)) map.set(material, []);
    map.get(material).push(nonIndexed);
  }

  addBox(w, h, d, x, y, z, material, tier = 1, rx = 0, ry = 0, rz = 0) {
    const geo = new THREE.BoxGeometry(w, h, d);
    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(rx, ry, rz));
    m.compose(new THREE.Vector3(x, y, z), q, new THREE.Vector3(1, 1, 1));
    this.addGeo(geo, material, tier, m);
  }

  /**
   * Adds a thin gold perimeter trim frame (4 edge bars) without filling the interior slab.
   */
  addGoldRim(w, h, d, x, y, z, thickness = 0.24, tier = 1) {
    const { goldMat } = this.materials;
    const hw = w * 0.5;
    const hd = d * 0.5;
    this.addBox(w + thickness, h, thickness, x, y, z - hd, goldMat, tier);
    this.addBox(w + thickness, h, thickness, x, y, z + hd, goldMat, tier);
    this.addBox(thickness, h, d - thickness, x - hw, y, z, goldMat, tier);
    this.addBox(thickness, h, d - thickness, x + hw, y, z, goldMat, tier);
  }

  addCylinder(rTop, rBot, h, segs, x, y, z, material, tier = 1, ry = 0) {
    const geo = new THREE.CylinderGeometry(rTop, rBot, h, segs);
    const m = new THREE.Matrix4().makeRotationY(ry).setPosition(x, y, z);
    this.addGeo(geo, material, tier, m);
  }

  addStoneBlockInstance(matrix, colorHex, tier = 1) {
    this.instances[tier].stoneBlocks.push({ matrix: matrix.clone(), colorHex });
  }

  addRoofSlateInstance(matrix, colorHex, tier = 1) {
    this.instances[tier].roofSlates.push({ matrix: matrix.clone(), colorHex });
  }

  addMossInstance(matrix, colorHex, tier = 1) {
    this.instances[tier].mossLeaves.push({ matrix: matrix.clone(), colorHex });
  }
}

// ============================================================================
// 5. MODULAR ARCHITECTURAL BUILDER FUNCTIONS
// ============================================================================

/**
 * Creates a bevelled stone block geometry used for the InstancedMesh masonry.
 */
function createBevelledBlockGeometry() {
  const shape = new THREE.Shape();
  const w = 0.46, h = 0.46;
  shape.moveTo(-w, -h);
  shape.lineTo(w, -h);
  shape.lineTo(w, h);
  shape.lineTo(-w, h);
  shape.closePath();

  const geo = new THREE.ExtrudeGeometry(shape, {
    depth: 0.78,
    bevelEnabled: true,
    bevelSegments: 1,
    steps: 1,
    bevelSize: 0.06,
    bevelThickness: 0.06
  });
  geo.center();
  return geo;
}

/**
 * Creates a thin, flat bevelled roof slate shingle lying in the XY plane (normal +Z).
 */
function createRoofSlateGeometry() {
  const shape = new THREE.Shape();
  shape.moveTo(-0.42, -0.44);
  shape.lineTo(0.42, -0.44);
  shape.lineTo(0.38, 0.44);
  shape.lineTo(-0.38, 0.44);
  shape.closePath();

  const geo = new THREE.ExtrudeGeometry(shape, {
    depth: 0.06,
    bevelEnabled: true,
    bevelSegments: 1,
    steps: 1,
    bevelSize: 0.018,
    bevelThickness: 0.018
  });
  geo.center();
  return geo;
}

/**
 * Creates a cluster of 4 pointed 2-sided ivy leaf quads arranged flat against the XY plane
 * and drooping downward (-Y) like real hanging ivy foliage.
 */
function createIvyClusterGeometry() {
  const leafGeos = [];
  const leafConfigs = [
    { dx: 0.0, dy: 0.0, dz: 0.01, rx: 0.18, ry: 0.0, rz: 0.0, s: 1.0 },
    { dx: -0.18, dy: -0.16, dz: 0.02, rx: 0.24, ry: -0.15, rz: -0.38, s: 0.85 },
    { dx: 0.18, dy: -0.14, dz: 0.02, rx: 0.22, ry: 0.15, rz: 0.36, s: 0.85 },
    { dx: 0.02, dy: -0.32, dz: 0.03, rx: 0.28, ry: 0.05, rz: 0.08, s: 0.78 }
  ];

  for (const cfg of leafConfigs) {
    // 2x2 subdivided plane shaped into a pointed cordate ivy leaf
    const g = new THREE.PlaneGeometry(0.36 * cfg.s, 0.44 * cfg.s, 2, 2);
    const pos = g.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      const vx = pos.getX(i);
      const vy = pos.getY(i);
      // Pointed bottom tip (vy < 0)
      if (vy < -0.05) {
        pos.setX(i, vx * 0.18);
      } else if (Math.abs(vy) <= 0.05) {
        // Wide lobed middle
        pos.setX(i, vx * 1.25);
        pos.setZ(i, 0.025 * cfg.s);
      } else {
        // Slight notch at top stem attachment
        pos.setX(i, vx * 0.65);
      }
    }
    g.computeVertexNormals();

    const m = new THREE.Matrix4();
    const q = new THREE.Quaternion().setFromEuler(new THREE.Euler(cfg.rx, cfg.ry, cfg.rz));
    m.compose(new THREE.Vector3(cfg.dx, cfg.dy, cfg.dz), q, new THREE.Vector3(1, 1, 1));
    g.applyMatrix4(m);
    leafGeos.push(g.index ? g.toNonIndexed() : g);
  }

  return BufferGeometryUtils.mergeGeometries(leafGeos, false);
}

/**
 * Builds the stepped stone plinth base, bevelled masonry courses, sloped corner/side
 * buttresses with inlaid gold arrow chevrons, and the wide front staircase (-Z only).
 */
function buildPlinth(ctx) {
  const { stoneMat, darkStoneMat, goldMat } = ctx.materials;

  // Solid inner cores for the two stepped plinth tiers
  ctx.addBox(34.2, 2.2, 34.2, 0, 1.1, 0, darkStoneMat, 1);
  ctx.addBox(31.2, 2.0, 31.2, 0, 3.2, 0, stoneMat, 1);
  // Thin gold rim moulding around top edge of plinth (not a solid gold floor)
  ctx.addGoldRim(31.3, 0.22, 31.3, 0, 4.1, 0, 0.24, 1);

  // Courtyard cobblestone floor slab
  ctx.addBox(27.0, 0.3, 27.0, 0, 4.15, 0, darkStoneMat, 1);

  // Instanced bevelled stone blocks around both stepped tiers of the plinth
  const dummy = new THREE.Object3D();
  // Richer stone block variation around #7E8089
  const stoneShades = [0x686b75, 0x767983, 0x828590, 0x6f727c, 0x8c8f9a, 0x61646e];

  const tierConfigs = [
    { y: 0.55, half: 17.25, count: 22, len: 1.52 },
    { y: 1.65, half: 16.95, count: 22, len: 1.48 },
    { y: 2.70, half: 15.70, count: 20, len: 1.52 },
    { y: 3.65, half: 15.45, count: 20, len: 1.48 }
  ];

  for (const cfg of tierConfigs) {
    for (let i = 0; i < cfg.count; i++) {
      const t = -cfg.half + (i + 0.5) * (2 * cfg.half / cfg.count);
      const jitter = (rand() - 0.5) * 0.08;

      // Front (-Z) — leave wider gap in center for the grand staircase
      if (Math.abs(t) > 4.8) {
        dummy.position.set(t, cfg.y, -cfg.half + jitter);
        dummy.rotation.set(0, 0, 0);
        dummy.scale.set(cfg.len, 0.95, 0.85);
        dummy.updateMatrix();
        ctx.addStoneBlockInstance(dummy.matrix, stoneShades[Math.floor(rand() * stoneShades.length)], 1);
      }

      // Back (+Z)
      dummy.position.set(t, cfg.y, cfg.half + jitter);
      dummy.rotation.set(0, 0, 0);
      dummy.scale.set(cfg.len, 0.95, 0.85);
      dummy.updateMatrix();
      ctx.addStoneBlockInstance(dummy.matrix, stoneShades[Math.floor(rand() * stoneShades.length)], 1);

      // Right side (-X)
      dummy.position.set(-cfg.half + jitter, cfg.y, t);
      dummy.rotation.set(0, Math.PI / 2, 0);
      dummy.scale.set(cfg.len, 0.95, 0.85);
      dummy.updateMatrix();
      ctx.addStoneBlockInstance(dummy.matrix, stoneShades[Math.floor(rand() * stoneShades.length)], 1);

      // Left side (+X)
      dummy.position.set(cfg.half + jitter, cfg.y, t);
      dummy.rotation.set(0, Math.PI / 2, 0);
      dummy.scale.set(cfg.len, 0.95, 0.85);
      dummy.updateMatrix();
      ctx.addStoneBlockInstance(dummy.matrix, stoneShades[Math.floor(rand() * stoneShades.length)], 1);
    }
  }

  // Helper to build a sloped stone buttress with inlaid gold arrow accent (seen in Keep_3.png)
  function addSlopedButtress(x, z, rotY) {
    const groupMat = new THREE.Matrix4().makeRotationY(rotY).setPosition(x, 0, z);

    const bGeo = new THREE.BoxGeometry(2.1, 3.8, 2.2);
    const pos = bGeo.attributes.position;
    for (let i = 0; i < pos.count; i++) {
      if (pos.getY(i) > 0) {
        if (pos.getZ(i) < 0) pos.setZ(i, pos.getZ(i) * 0.35);
        pos.setX(i, pos.getX(i) * 0.78);
      }
    }
    bGeo.computeVertexNormals();
    const mBody = groupMat.clone().multiply(new THREE.Matrix4().makeTranslation(0, 1.9, 0));
    ctx.addGeo(bGeo, stoneMat, 1, mBody);

    const arrowShape = new THREE.Shape();
    arrowShape.moveTo(0, 1.15);
    arrowShape.lineTo(0.26, 0.72);
    arrowShape.lineTo(0.20, -0.95);
    arrowShape.lineTo(-0.20, -0.95);
    arrowShape.lineTo(-0.26, 0.72);
    arrowShape.closePath();

    const arrowGeo = new THREE.ExtrudeGeometry(arrowShape, {
      depth: 0.16,
      bevelEnabled: true,
      bevelSegments: 1,
      bevelSize: 0.03,
      bevelThickness: 0.03
    });
    const mArrow = groupMat
      .clone()
      .multiply(new THREE.Matrix4().makeTranslation(0, 1.85, -0.78))
      .multiply(new THREE.Matrix4().makeRotationX(0.36));
    ctx.addGeo(arrowGeo, goldMat, 1, mArrow);
  }

  const cornerSpots = [
    { x: -13.6, z: -16.8, r: 0 },
    { x: 13.6, z: -16.8, r: 0 },
    { x: -16.8, z: -13.6, r: Math.PI / 2 },
    { x: -16.8, z: 13.6, r: Math.PI / 2 },
    { x: 16.8, z: -13.6, r: -Math.PI / 2 },
    { x: 16.8, z: 13.6, r: -Math.PI / 2 },
    { x: -13.6, z: 16.8, r: Math.PI },
    { x: 13.6, z: 16.8, r: Math.PI }
  ];
  for (const b of cornerSpots) {
    addSlopedButtress(b.x, b.z, b.r);
  }

  // --- Wide, Prominent Front Staircase (ONLY on Front -Z, framed by the two gate pillars) ---
  const numSteps = 12;
  const stairW = 8.4;
  const startZ = -14.0;
  const endZ = -21.8;

  // Top landing platform directly between the two gate pillars
  ctx.addBox(stairW, 4.0, 1.8, 0, 2.0, -13.6, stoneMat, 1);

  for (let s = 0; s < numSteps; s++) {
    const frac = s / numSteps;
    const stepH = 4.0 * (1 - frac);
    const z = startZ + frac * (endZ - startZ);
    const depth = (Math.abs(endZ - startZ) / numSteps) + 0.18;
    ctx.addBox(stairW, stepH, depth, 0, stepH / 2, z, stoneMat, 1);

    // Individual bevelled stone treads across each step
    for (let k = -3; k <= 3; k++) {
      dummy.position.set(k * 1.14, stepH - 0.16, z - 0.06);
      dummy.rotation.set(0, 0, 0);
      dummy.scale.set(1.08, 0.36, 0.72);
      dummy.updateMatrix();
      ctx.addStoneBlockInstance(dummy.matrix, stoneShades[(s + k + 12) % stoneShades.length], 1);
    }
  }

  // Staircase side stringer walls, sloped buttress prows, and gold-capped bollards
  for (const side of [-1, 1]) {
    const sx = side * 4.65;
    // Stepped stone parapet wall alongside stairs
    ctx.addBox(1.2, 3.3, 5.4, sx, 1.65, -17.4, stoneMat, 1);
    ctx.addGoldRim(1.26, 0.18, 5.46, sx, 3.32, -17.4, 0.14, 1);

    // Sloped front prow with gold arrow inlay at the foot of each stair wall
    addSlopedButtress(sx, -20.6, 0);

    // Square bollard pillar with gold pyramid cap at stair mid-landing
    const bz = -17.5;
    ctx.addBox(1.4, 2.3, 1.4, sx, 4.15, bz, stoneMat, 1);
    ctx.addGoldRim(1.52, 0.2, 1.52, sx, 5.3, bz, 0.16, 1);
    ctx.addCylinder(0.04, 1.0, 1.1, 4, sx, 5.95, bz, goldMat, 1, Math.PI / 4);
  }
}

/**
 * Builds an arched medieval window with recessed stone frame, gold sill, and warm glowing glass.
 */
function buildArchedWindow(ctx, x, y, z, rotY, scale = 1.0, tier = 1) {
  const { stoneMat, goldMat, windowGlowMat } = ctx.materials;
  const m = new THREE.Matrix4().makeRotationY(rotY).setPosition(x, y, z);

  const w = 0.95 * scale;
  const h = 2.0 * scale;

  const frameShape = new THREE.Shape();
  frameShape.moveTo(-w * 0.7, -h * 0.5);
  frameShape.lineTo(w * 0.7, -h * 0.5);
  frameShape.lineTo(w * 0.7, h * 0.25);
  frameShape.lineTo(0, h * 0.72);
  frameShape.lineTo(-w * 0.7, h * 0.25);
  frameShape.closePath();

  const frameGeo = new THREE.ExtrudeGeometry(frameShape, {
    depth: 0.32 * scale,
    bevelEnabled: true,
    bevelSegments: 1,
    bevelSize: 0.04 * scale,
    bevelThickness: 0.04 * scale
  });
  frameGeo.center();
  ctx.addGeo(frameGeo, stoneMat, tier, m);

  const paneShape = new THREE.Shape();
  paneShape.moveTo(-w * 0.42, -h * 0.38);
  paneShape.lineTo(w * 0.42, -h * 0.38);
  paneShape.lineTo(w * 0.42, h * 0.18);
  paneShape.lineTo(0, h * 0.5);
  paneShape.lineTo(-w * 0.42, h * 0.18);
  paneShape.closePath();

  const paneGeo = new THREE.ExtrudeGeometry(paneShape, {
    depth: 0.16 * scale,
    bevelEnabled: false
  });
  paneGeo.center();
  const mPane = m.clone().multiply(new THREE.Matrix4().makeTranslation(0, 0, 0.09 * scale));
  ctx.addGeo(paneGeo, windowGlowMat, tier, mPane);

  const sillGeo = new THREE.BoxGeometry(w * 1.55, 0.18 * scale, 0.42 * scale);
  const mSill = m.clone().multiply(new THREE.Matrix4().makeTranslation(0, -h * 0.52, 0.06 * scale));
  ctx.addGeo(sillGeo, goldMat, tier, mSill);
}

/**
 * Helper to place a row of bevelled stone crenellation merlons with stone capstones.
 */
function addMerlonRow(ctx, x1, z1, x2, z2, y, count, tier = 1) {
  const { stoneMat, darkStoneMat } = ctx.materials;
  const dx = x2 - x1;
  const dz = z2 - z1;
  const angle = Math.atan2(dx, dz);

  for (let i = 0; i < count; i++) {
    const t = count === 1 ? 0.5 : i / (count - 1);
    const mx = x1 + dx * t;
    const mz = z1 + dz * t;
    ctx.addBox(0.95, 0.92, 0.95, mx, y + 0.46, mz, stoneMat, tier, 0, angle, 0);
    ctx.addBox(1.04, 0.14, 1.04, mx, y + 0.98, mz, darkStoneMat, tier, 0, angle, 0);
  }
}

/**
 * Builds the 4 perimeter curtain walls, thin exterior gold trim bands, stone crenellations,
 * left interior stepped wall-walk (+X side), and back postern door (+Z side).
 */
function buildWalls(ctx) {
  const { stoneMat, darkStoneMat, goldMat, woodMat, ironMat, roofBaseMat } = ctx.materials;
  const wallY = 4.0;
  const wallH = 6.2;
  const wallMidY = wallY + wallH / 2; // 7.1
  const wallTopY = wallY + wallH;     // 10.2
  const dummy = new THREE.Object3D();
  const stoneShades = [0x686b75, 0x767983, 0x828590, 0x6f727c, 0x8c8f9a];

  // --- 1. Front Curtain Wall (-Z = -13.0), split either side of the Gatehouse ---
  for (const side of [-1, 1]) {
    const cx = side * 8.4;
    ctx.addBox(5.8, wallH, 2.1, cx, wallMidY, -13.0, stoneMat, 1);
    // Thin exterior gold frieze strip (only on outer face, not covering top of wall)
    ctx.addBox(6.0, 0.34, 0.22, cx, wallTopY - 0.55, -14.08, goldMat, 1);
    // Blue slate wall-walk strip (seen in Keep_3.png behind front merlons)
    ctx.addBox(5.6, 0.22, 1.4, cx, wallTopY + 0.05, -13.0, roofBaseMat, 1);
    // Crenellations on front
    addMerlonRow(ctx, cx - 2.0, -13.65, cx + 2.0, -13.65, wallTopY, 3, 1);
    // Warm lit window on each front curtain wing
    buildArchedWindow(ctx, side * 8.4, 6.8, -14.05, Math.PI, 1.0, 1);

    for (let r = 0; r < 4; r++) {
      for (let c = -2; c <= 2; c++) {
        if (Math.abs(c) <= 1 && r >= 1 && r <= 2) continue;
        dummy.position.set(cx + c * 1.1, 4.8 + r * 1.25, -14.02);
        dummy.rotation.set(0, 0, 0);
        dummy.scale.set(1.0, 0.95, 0.35);
        dummy.updateMatrix();
        ctx.addStoneBlockInstance(dummy.matrix, stoneShades[(r + c + 4) % stoneShades.length], 1);
      }
    }
  }

  // --- 2. Viewer's Left Curtain Wall (+X = +13.0) + Stepped Stone Wall-Walk (per plan) ---
  ctx.addBox(2.1, wallH, 21.0, 13.0, wallMidY, 0, stoneMat, 1);
  // Thin exterior gold frieze strip on outer +X face
  ctx.addBox(0.22, 0.34, 21.2, 14.08, wallTopY - 0.55, 0, goldMat, 1);
  addMerlonRow(ctx, 13.65, -9.0, 13.65, 9.0, wallTopY, 10, 1);
  addMerlonRow(ctx, 12.35, -8.5, 12.35, 8.5, wallTopY, 8, 1);

  buildArchedWindow(ctx, 14.05, 6.8, -5.5, Math.PI / 2, 0.95, 1);
  buildArchedWindow(ctx, 14.05, 6.8, 5.5, Math.PI / 2, 0.95, 1);

  // Left-side (+X) Interior Stepped Wall-Walk — pure stone top (no flat gold slab)
  ctx.addBox(2.4, 3.6, 14.0, 10.8, 5.8, -0.5, darkStoneMat, 1);
  ctx.addBox(2.5, 0.22, 14.1, 10.8, 7.65, -0.5, stoneMat, 1);
  for (let s = 0; s < 6; s++) {
    const sh = (s + 1) * 0.55;
    ctx.addBox(2.2, sh, 0.85, 10.8, 4.0 + sh / 2, -9.8 + s * 0.8, stoneMat, 1);
  }

  // --- 3. Viewer's Right Curtain Wall (-X = -13.0, Colonnade Side) ---
  ctx.addBox(2.1, wallH, 21.0, -13.0, wallMidY, 0, stoneMat, 1);
  // Thin exterior gold frieze strip on outer -X face
  ctx.addBox(0.22, 0.34, 21.2, -14.08, wallTopY - 0.55, 0, goldMat, 1);
  addMerlonRow(ctx, -13.65, -9.0, -13.65, 9.0, wallTopY, 10, 1);

  // --- 4. Back Curtain Wall (+Z = +13.0) with small Postern Door ---
  ctx.addBox(21.0, wallH, 2.1, 0, wallMidY, 13.0, stoneMat, 1);
  // Thin exterior gold frieze strip on outer +Z face
  ctx.addBox(21.2, 0.34, 0.22, 0, wallTopY - 0.55, 14.08, goldMat, 1);
  addMerlonRow(ctx, -9.0, 13.65, 9.0, 13.65, wallTopY, 10, 1);

  // Small postern door on the back wall (x = 4.5, z = 14.05)
  ctx.addBox(1.8, 2.7, 0.35, 4.5, 5.35, 14.02, darkStoneMat, 1);
  ctx.addBox(1.35, 2.35, 0.25, 4.5, 5.2, 14.1, woodMat, 1);
  ctx.addBox(1.4, 0.14, 0.3, 4.5, 5.8, 14.12, ironMat, 1);
  ctx.addBox(1.4, 0.14, 0.3, 4.5, 4.7, 14.12, ironMat, 1);
}

/**
 * Builds the Two-Storey Arched Colonnade on the viewer's RIGHT (-X side)
 * facing both the open inner courtyard and the exterior arcade (seen in Keep_3.png).
 */
function buildColonnade(ctx) {
  const { stoneMat, darkStoneMat, goldMat } = ctx.materials;
  const colX = -10.2;
  const zPositions = [-7.5, -4.5, -1.5, 1.5, 4.5, 7.5];

  // Lower & Upper stone deck slabs connecting colonnade to -X curtain wall (stone top, thin gold edge strip)
  ctx.addBox(3.4, 0.45, 18.2, -11.2, 7.2, 0, stoneMat, 2);
  ctx.addBox(0.22, 0.24, 18.2, -9.5, 7.35, 0, goldMat, 2);

  ctx.addBox(3.4, 0.45, 18.2, -11.2, 10.2, 0, darkStoneMat, 2);
  ctx.addBox(0.22, 0.26, 18.2, -9.5, 10.35, 0, goldMat, 2);

  // Columns and arches for both storeys facing the courtyard (-X = -10.2)
  // Plus exterior blind-arcade columns & arches on the outer -X wall (-14.15) as seen in Keep_3.png!
  const arcadePlanes = [
    { x: colX, storeys: [0, 1] },
    { x: -14.15, storeys: [0] }
  ];

  for (const plane of arcadePlanes) {
    for (const storey of plane.storeys) {
      const baseY = storey === 0 ? 4.2 : 7.55;
      const colH = storey === 0 ? 2.8 : 2.55;

      for (let i = 0; i < zPositions.length; i++) {
        const z = zPositions[i];
        ctx.addBox(0.85, 0.32, 0.85, plane.x, baseY + 0.16, z, darkStoneMat, 2);
        ctx.addCylinder(0.38, 0.42, 0.16, 12, plane.x, baseY + 0.38, z, goldMat, 2);
        ctx.addCylinder(0.31, 0.34, colH - 0.7, 12, plane.x, baseY + colH * 0.5, z, stoneMat, 2);
        ctx.addCylinder(0.44, 0.33, 0.22, 12, plane.x, baseY + colH - 0.22, z, goldMat, 2);
        ctx.addBox(0.88, 0.2, 0.88, plane.x, baseY + colH - 0.06, z, stoneMat, 2);

        if (i < zPositions.length - 1) {
          const midZ = (z + zPositions[i + 1]) * 0.5;
          const span = zPositions[i + 1] - z;
          const archSegs = 7;
          for (let a = 0; a < archSegs; a++) {
            const theta = Math.PI * ((a + 0.5) / archSegs);
            const az = midZ - Math.cos(theta) * (span * 0.38);
            const ay = baseY + colH - 0.35 + Math.sin(theta) * 0.65;
            ctx.addBox(0.68, 0.42, 0.52, plane.x, ay, az, stoneMat, 2, -theta + Math.PI / 2, 0, 0);
          }
        }
      }
    }
  }
}

/**
 * Builds a steep 4-sided pyramid blue-slate roof with gold corner hip ridges,
 * thin gold eaves cornice, flush overlapping slate rows (InstancedMesh),
 * gold finial spire, and an optional waving swallow-tail pennant.
 */
function buildRoof(ctx, x, y, z, width, height, depth, options = {}) {
  const {
    tier = 1,
    hasFlag = true,
    flagScale = 1.0,
    slateRows = 9,
    cornerSpires = true
  } = options;
  const { roofBaseMat, goldMat } = ctx.materials;

  // Thin gold eaves rim around the base of the roof
  ctx.addGoldRim(width + 0.3, 0.26, depth + 0.3, x, y + 0.13, z, 0.22, tier);

  // Inner blue slate pyramid core
  const rCore = (width * 0.495) * Math.SQRT2;
  const coreGeo = new THREE.ConeGeometry(rCore, height, 4);
  const mCore = new THREE.Matrix4()
    .makeTranslation(x, y + height * 0.5 + 0.2, z)
    .multiply(new THREE.Matrix4().makeScale(1, 1, depth / width))
    .multiply(new THREE.Matrix4().makeRotationY(Math.PI / 4));
  ctx.addGeo(coreGeo, roofBaseMat, tier, mCore);

  // 4 Gold Hip Ridges along the pyramid diagonals
  const halfW = width * 0.5;
  const halfD = depth * 0.5;
  const corners = [
    [-halfW, -halfD],
    [halfW, -halfD],
    [halfW, halfD],
    [-halfW, halfD]
  ];
  for (const [cx, cz] of corners) {
    const start = new THREE.Vector3(x + cx, y + 0.22, z + cz);
    const end = new THREE.Vector3(x, y + height + 0.22, z);
    const dir = new THREE.Vector3().subVectors(end, start);
    const len = dir.length();
    const mid = new THREE.Vector3().addVectors(start, end).multiplyScalar(0.5);

    const ridgeGeo = new THREE.BoxGeometry(0.22, len, 0.22);
    const quat = new THREE.Quaternion().setFromUnitVectors(
      new THREE.Vector3(0, 1, 0),
      dir.clone().normalize()
    );
    const mRidge = new THREE.Matrix4().compose(mid, quat, new THREE.Vector3(1, 1, 1));
    ctx.addGeo(ridgeGeo, goldMat, tier, mRidge);

    if (cornerSpires) {
      const sx = x + cx * 0.94;
      const sz = z + cz * 0.94;
      ctx.addBox(0.48, 0.6, 0.48, sx, y + 0.52, sz, goldMat, tier);
      ctx.addCylinder(0.03, 0.32, 1.1, 4, sx, y + 1.35, sz, goldMat, tier, Math.PI / 4);
    }
  }

  // Flush, Overlapping Roof Slate Shingles (InstancedMesh) on all 4 pyramid faces
  const dummy = new THREE.Object3D();
  dummy.rotation.order = 'YXZ'; // Yaw to face outward, then pitch around local X to match face slope
  const slateShades = [0xc2d4ff, 0x9eb8f5, 0xd4e2ff, 0x8ea9eb, 0xb5c9f7];

  // Angle of face from vertical (0 = vertical wall, >0 = leaning inward toward apex)
  const slopeFB = Math.atan2(halfD, height);
  const slopeLR = Math.atan2(halfW, height);
  const shingleOverlapTilt = 0.045; // slight overlap so bottom edge steps over the row below

  for (let r = 0; r < slateRows; r++) {
    const v = (r + 0.45) / slateRows; // 0 = eaves, 1 = apex
    const rowY = y + 0.2 + v * height;
    const spanW = width * (1 - v);
    const spanD = depth * (1 - v);

    // Front (-Z) and Back (+Z) pyramid faces
    const countX = Math.max(1, Math.floor((spanW - 0.45) / 0.76));
    const distZ = halfD * (1 - v) + 0.04;
    for (let i = 0; i < countX; i++) {
      const u = countX === 1 ? 0 : (i / (countX - 1) - 0.5) * Math.max(0, spanW - 0.9);

      // Front (-Z) face: outward yaw = Math.PI, pitch = -(slopeFB - shingleOverlapTilt)
      dummy.position.set(x + u, rowY, z - distZ);
      dummy.rotation.set(-(slopeFB - shingleOverlapTilt), Math.PI, 0);
      dummy.scale.set(0.88, 0.92, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + i) % slateShades.length], tier);

      // Back (+Z) face: outward yaw = 0, pitch = -(slopeFB - shingleOverlapTilt)
      dummy.position.set(x + u, rowY, z + distZ);
      dummy.rotation.set(-(slopeFB - shingleOverlapTilt), 0, 0);
      dummy.scale.set(0.88, 0.92, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + i + 2) % slateShades.length], tier);
    }

    // Right (-X) and Left (+X) pyramid faces
    const countZ = Math.max(1, Math.floor((spanD - 0.45) / 0.76));
    const distX = halfW * (1 - v) + 0.04;
    for (let i = 0; i < countZ; i++) {
      const u = countZ === 1 ? 0 : (i / (countZ - 1) - 0.5) * Math.max(0, spanD - 0.9);

      // Right (-X) face: outward yaw = -Math.PI / 2
      dummy.position.set(x - distX, rowY, z + u);
      dummy.rotation.set(-(slopeLR - shingleOverlapTilt), -Math.PI / 2, 0);
      dummy.scale.set(0.88, 0.92, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + i + 1) % slateShades.length], tier);

      // Left (+X) face: outward yaw = +Math.PI / 2
      dummy.position.set(x + distX, rowY, z + u);
      dummy.rotation.set(-(slopeLR - shingleOverlapTilt), Math.PI / 2, 0);
      dummy.scale.set(0.88, 0.92, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + i + 3) % slateShades.length], tier);
    }
  }

  // Gold Apex Finial & Waving Swallow-Tail Pennant
  const peakY = y + height + 0.22;
  ctx.addCylinder(0.30, 0.46, 0.48, 8, x, peakY + 0.2, z, goldMat, tier);
  ctx.addCylinder(0.16, 0.16, 0.34, 8, x, peakY + 0.52, z, goldMat, tier);

  if (hasFlag) {
    const poleH = 3.2 * flagScale;
    ctx.addCylinder(0.08 * flagScale, 0.12 * flagScale, poleH, 8, x, peakY + 0.5 + poleH * 0.5, z, goldMat, tier);
    ctx.addCylinder(0.01, 0.22 * flagScale, 0.65 * flagScale, 6, x, peakY + 0.5 + poleH + 0.3, z, goldMat, tier);
    buildPennant(ctx, x, peakY + 0.5 + poleH * 0.68, z, flagScale, tier);
  } else {
    ctx.addCylinder(0.02, 0.25, 1.15, 6, x, peakY + 1.05, z, goldMat, tier);
  }
}

/**
 * Builds a subdivided swallow-tail blue pennant oriented so it streams to the right
 * from the default (-36, 33, -42) camera view, matching Keep_3.png.
 */
function buildPennant(ctx, x, y, z, scale = 1.0, tier = 1) {
  const { pennantMat, goldMat } = ctx.materials;
  const len = 3.8 * scale;
  const h = 1.15 * scale;
  const segsX = 24;
  const segsY = 6;

  const geo = new THREE.PlaneGeometry(len, h, segsX, segsY);
  geo.translate(len * 0.5, 0, 0);

  const pos = geo.attributes.position;
  const uv = geo.attributes.uv;
  for (let i = 0; i < pos.count; i++) {
    const u = uv.getX(i);
    const v = uv.getY(i);
    const taper = 1.0 - u * 0.55;
    const drapeY = Math.sin(u * Math.PI * 1.35) * 0.28 * scale - u * 0.25 * scale;
    const notch = u > 0.82 && Math.abs(v - 0.5) < 0.24 ? -0.35 * scale * ((u - 0.82) / 0.18) : 0;

    pos.setX(i, pos.getX(i) + notch);
    pos.setY(i, (v - 0.5) * h * taper + drapeY);
  }
  geo.computeVertexNormals();

  // Rotate pennant so it streams toward (-X, +Z), i.e. to the right on screen from (-36, 33, -42)
  const m = new THREE.Matrix4()
    .makeTranslation(x, y, z)
    .multiply(new THREE.Matrix4().makeRotationY(Math.PI * 0.78));
  ctx.addGeo(geo, pennantMat, tier, m);

  ctx.addCylinder(0.16 * scale, 0.16 * scale, 0.14 * scale, 8, x, y + h * 0.42, z, goldMat, tier);
  ctx.addCylinder(0.16 * scale, 0.16 * scale, 0.14 * scale, 8, x, y - h * 0.42, z, goldMat, tier);
}

/**
 * Builds a vertical hanging blue banner with gold crossbar, gold end finials,
 * and gold-trimmed pointed cloth.
 */
function buildHangingBanner(ctx, x, y, z, rotY, width = 1.55, height = 3.6, tier = 1) {
  const { bannerMat, goldMat } = ctx.materials;
  const mGroup = new THREE.Matrix4().makeRotationY(rotY).setPosition(x, y, z);

  const barGeo = new THREE.CylinderGeometry(0.08, 0.08, width + 0.45, 8);
  barGeo.rotateZ(Math.PI / 2);
  ctx.addGeo(barGeo, goldMat, tier, mGroup);

  for (const side of [-1, 1]) {
    const capGeo = new THREE.ConeGeometry(0.16, 0.32, 6);
    capGeo.rotateZ(-side * Math.PI / 2);
    const mCap = mGroup
      .clone()
      .multiply(new THREE.Matrix4().makeTranslation(side * (width * 0.5 + 0.32), 0, 0));
    ctx.addGeo(capGeo, goldMat, tier, mCap);
  }

  const geo = new THREE.PlaneGeometry(width, height, 8, 20);
  geo.translate(0, -height * 0.5, 0);
  const pos = geo.attributes.position;
  const uv = geo.attributes.uv;
  for (let i = 0; i < pos.count; i++) {
    const u = uv.getX(i);
    const v = uv.getY(i);
    if (v < 0.24) {
      const t = v / 0.24;
      pos.setX(i, pos.getX(i) * t);
    }
    pos.setZ(i, Math.sin((1 - v) * Math.PI) * 0.08 + (1 - Math.abs(u - 0.5) * 2) * 0.03);
  }
  geo.computeVertexNormals();
  ctx.addGeo(geo, bannerMat, tier, mGroup);
}

/**
 * Builds one of the 4 square corner towers or the 1 back-wall tower (Items 1 & 2 on the plan).
 */
function buildTower(ctx, x, z, options = {}) {
  const {
    width = 5.4,
    baseY = 4.0,
    height = 10.2,
    roofHeight = 5.6,
    bannerSides = [],
    windowSides = [],
    hasFlag = true,
    tier = 1
  } = options;
  const { stoneMat, darkStoneMat } = ctx.materials;

  const midY = baseY + height * 0.5;
  const topY = baseY + height;

  // Main tower shaft
  ctx.addBox(width, height, width, x, midY, z, stoneMat, tier);
  // Moulded stone base skirt + thin gold rim
  ctx.addBox(width + 0.45, 1.2, width + 0.45, x, baseY + 0.6, z, darkStoneMat, tier);
  ctx.addGoldRim(width + 0.52, 0.2, width + 0.52, x, baseY + 1.25, z, 0.18, tier);

  // Corner vertical stone pilasters
  const pOff = width * 0.48;
  for (const [px, pz] of [[-pOff, -pOff], [pOff, -pOff], [pOff, pOff], [-pOff, pOff]]) {
    ctx.addBox(0.65, height, 0.65, x + px, midY, z + pz, stoneMat, tier);
  }

  // Thin mid-height & upper gold trim bands around the tower perimeter
  ctx.addGoldRim(width + 0.46, 0.28, width + 0.46, x, baseY + height * 0.58, z, 0.2, tier);
  ctx.addGoldRim(width + 0.72, 0.36, width + 0.72, x, topY - 0.9, z, 0.22, tier);

  // Overhanging stone parapet collar with crenellation merlons on all 4 sides
  const paraW = width + 0.65;
  ctx.addBox(paraW, 0.9, paraW, x, topY - 0.35, z, stoneMat, tier);
  const mOff = paraW * 0.5 - 0.38;
  addMerlonRow(ctx, x - mOff, z - mOff, x + mOff, z - mOff, topY, 4, tier);
  addMerlonRow(ctx, x - mOff, z + mOff, x + mOff, z + mOff, topY, 4, tier);
  addMerlonRow(ctx, x - mOff, z - mOff, x - mOff, z + mOff, topY, 4, tier);
  addMerlonRow(ctx, x + mOff, z - mOff, x + mOff, z + mOff, topY, 4, tier);

  // Face mapping: 'right' in scene coordinates is -X (colonnade side), 'left' is +X
  const faceMap = {
    front: { dx: 0, dz: -width * 0.5 - 0.05, rot: Math.PI },
    back: { dx: 0, dz: width * 0.5 + 0.05, rot: 0 },
    right: { dx: -width * 0.5 - 0.05, dz: 0, rot: -Math.PI / 2 },
    left: { dx: width * 0.5 + 0.05, dz: 0, rot: Math.PI / 2 }
  };

  for (const side of windowSides) {
    const f = faceMap[side];
    if (f) {
      buildArchedWindow(ctx, x + f.dx, baseY + height * 0.35, z + f.dz, f.rot, 0.92, tier);
    }
  }

  for (const side of bannerSides) {
    const f = faceMap[side];
    if (f) {
      const bx = x + (f.dx !== 0 ? Math.sign(f.dx) * (width * 0.5 + 0.32) : 0);
      const bz = z + (f.dz !== 0 ? Math.sign(f.dz) * (width * 0.5 + 0.32) : 0);
      buildHangingBanner(ctx, bx, topY - 1.35, bz, f.rot + Math.PI, 1.5, 3.8, tier);
    }
  }

  buildRoof(ctx, x, topY + 0.05, z, width - 0.85, roofHeight, width - 0.85, {
    tier,
    hasFlag,
    flagScale: 0.9,
    slateRows: 8,
    cornerSpires: true
  });
}

/**
 * Builds a gabled roof dormer window with gold bargeboards, slate roof, and warm lit window.
 */
function buildDormer(ctx, x, y, z, rotY, scale = 1.0, tier = 3) {
  const { stoneMat, goldMat, roofBaseMat } = ctx.materials;
  const m = new THREE.Matrix4().makeRotationY(rotY).setPosition(x, y, z);

  const bodyGeo = new THREE.BoxGeometry(1.65 * scale, 2.1 * scale, 1.8 * scale);
  ctx.addGeo(bodyGeo, stoneMat, tier, m);

  const roofGeo = new THREE.ConeGeometry(1.45 * scale, 1.35 * scale, 4);
  const mRoof = m
    .clone()
    .multiply(new THREE.Matrix4().makeTranslation(0, 1.55 * scale, 0))
    .multiply(new THREE.Matrix4().makeRotationY(Math.PI / 4));
  ctx.addGeo(roofGeo, roofBaseMat, tier, mRoof);

  const gLeft = new THREE.BoxGeometry(0.18 * scale, 1.45 * scale, 0.24 * scale);
  const mLeft = m
    .clone()
    .multiply(new THREE.Matrix4().makeTranslation(-0.48 * scale, 1.45 * scale, 0.88 * scale))
    .multiply(new THREE.Matrix4().makeRotationZ(-0.52));
  ctx.addGeo(gLeft, goldMat, tier, mLeft);

  const mRight = m
    .clone()
    .multiply(new THREE.Matrix4().makeTranslation(0.48 * scale, 1.45 * scale, 0.88 * scale))
    .multiply(new THREE.Matrix4().makeRotationZ(0.52));
  ctx.addGeo(gLeft, goldMat, tier, mRight);

  const spikeGeo = new THREE.ConeGeometry(0.14 * scale, 0.55 * scale, 4);
  const mSpike = m
    .clone()
    .multiply(new THREE.Matrix4().makeTranslation(0, 2.35 * scale, 0.82 * scale));
  ctx.addGeo(spikeGeo, goldMat, tier, mSpike);

  const worldPos = new THREE.Vector3(0, -0.15 * scale, 0.86 * scale).applyMatrix4(m);
  buildArchedWindow(ctx, worldPos.x, worldPos.y, worldPos.z, rotY, 0.65 * scale, tier);
}

/**
 * Builds the large glowing teal diamond rune set in an ornate gold frame
 * (Items 7 & 8 on the plan: one above the gate, one on the keep's front facade).
 */
function buildRune(ctx, x, y, z, scale = 1.0, tier = 1) {
  const { goldMat, darkStoneMat, runeOuterMat, runeCoreMat } = ctx.materials;

  const outerShape = new THREE.Shape();
  const R = 1.85 * scale;
  outerShape.moveTo(0, R);
  outerShape.lineTo(R * 0.82, 0);
  outerShape.lineTo(0, -R);
  outerShape.lineTo(-R * 0.82, 0);
  outerShape.closePath();

  const innerHole = new THREE.Path();
  const r = 1.25 * scale;
  innerHole.moveTo(0, r);
  innerHole.lineTo(r * 0.82, 0);
  innerHole.lineTo(0, -r);
  innerHole.lineTo(-r * 0.82, 0);
  innerHole.closePath();
  outerShape.holes.push(innerHole);

  const frameGeo = new THREE.ExtrudeGeometry(outerShape, {
    depth: 0.42 * scale,
    bevelEnabled: true,
    bevelSegments: 2,
    bevelSize: 0.08 * scale,
    bevelThickness: 0.08 * scale
  });
  frameGeo.center();
  ctx.addGeo(frameGeo, goldMat, tier, new THREE.Matrix4().makeTranslation(x, y, z));

  const backGeo = new THREE.BoxGeometry(r * 1.15, r * 1.15, 0.22 * scale);
  const mBack = new THREE.Matrix4()
    .makeTranslation(x, y, z + 0.08 * scale)
    .multiply(new THREE.Matrix4().makeRotationZ(Math.PI / 4));
  ctx.addGeo(backGeo, darkStoneMat, tier, mBack);

  const crystalShape = new THREE.Shape();
  crystalShape.moveTo(0, r * 0.98);
  crystalShape.lineTo(r * 0.8, 0);
  crystalShape.lineTo(0, -r * 0.98);
  crystalShape.lineTo(-r * 0.8, 0);
  crystalShape.closePath();

  const crystalGeo = new THREE.ExtrudeGeometry(crystalShape, {
    depth: 0.26 * scale,
    bevelEnabled: true,
    bevelSegments: 2,
    bevelSize: 0.06 * scale,
    bevelThickness: 0.08 * scale
  });
  crystalGeo.center();
  ctx.addGeo(
    crystalGeo,
    runeOuterMat,
    tier,
    new THREE.Matrix4().makeTranslation(x, y, z - 0.04 * scale)
  );

  const glyphShape = new THREE.Shape();
  const g1 = 0.68 * scale;
  const g2 = 0.22 * scale;
  glyphShape.moveTo(0, g1);
  glyphShape.lineTo(g2, g2);
  glyphShape.lineTo(g1 * 0.85, 0);
  glyphShape.lineTo(g2, -g2);
  glyphShape.lineTo(0, -g1);
  glyphShape.lineTo(-g2, -g2);
  glyphShape.lineTo(-g1 * 0.85, 0);
  glyphShape.lineTo(-g2, g2);
  glyphShape.closePath();

  const glyphGeo = new THREE.ExtrudeGeometry(glyphShape, {
    depth: 0.18 * scale,
    bevelEnabled: true,
    bevelSegments: 1,
    bevelSize: 0.04 * scale,
    bevelThickness: 0.04 * scale
  });
  glyphGeo.center();
  ctx.addGeo(
    glyphGeo,
    runeCoreMat,
    tier,
    new THREE.Matrix4().makeTranslation(x, y, z - 0.16 * scale)
  );

  const light = new THREE.PointLight(PALETTE.runeOuter, 4.5, 12);
  light.position.set(x, y, z - 0.85 * scale);
  ctx.pointLights.push({ light, type: 'rune', baseIntensity: 4.5, phase: rand() * 6.28 });
}

/**
 * Builds the tall Central Keep offset to the viewer's LEFT (+X = +3.2) and BACK (+Z = +3.0)
 * of centre (Item 3 on plan), plus the blue-roofed Gallery Bridge (Item 4) connecting the
 * keep's -X face to the right-side colonnade (-X).
 */
function buildKeep(ctx) {
  const { stoneMat, darkStoneMat, goldMat, roofBaseMat } = ctx.materials;
  const kx = 3.2;
  const kz = 3.0;
  const kw = 10.0;
  const kd = 10.0;

  // Lower Keep Body (present in all Tiers 1–3)
  const lowerBaseY = 4.0;
  const lowerH = 12.0; // reaches y = 16.0
  ctx.addBox(kw, lowerH, kd, kx, lowerBaseY + lowerH * 0.5, kz, stoneMat, 1);
  ctx.addBox(kw + 0.5, 1.4, kd + 0.5, kx, lowerBaseY + 0.7, kz, darkStoneMat, 1);
  ctx.addGoldRim(kw + 0.62, 0.28, kd + 0.62, kx, lowerBaseY + 1.45, kz, 0.2, 1);

  // Corner vertical pilasters with thin gold collars
  const pOff = kw * 0.49;
  for (const [dx, dz] of [[-pOff, -pOff], [pOff, -pOff], [pOff, pOff], [-pOff, pOff]]) {
    ctx.addBox(1.15, lowerH, 1.15, kx + dx, lowerBaseY + lowerH * 0.5, kz + dz, stoneMat, 1);
    ctx.addGoldRim(1.3, 0.28, 1.3, kx + dx, lowerBaseY + 7.5, kz + dz, 0.16, 1);
    ctx.addGoldRim(1.35, 0.34, 1.35, kx + dx, lowerBaseY + lowerH - 0.25, kz + dz, 0.18, 1);
  }

  // Lower-mid Keep Windows
  buildArchedWindow(ctx, kx - 2.9, 13.5, kz - kd * 0.5 - 0.05, Math.PI, 1.05, 1);
  buildArchedWindow(ctx, kx + 2.9, 13.5, kz - kd * 0.5 - 0.05, Math.PI, 1.05, 1);
  buildArchedWindow(ctx, kx - kw * 0.5 - 0.05, 13.5, kz - 2.2, -Math.PI / 2, 1.1, 1);
  buildArchedWindow(ctx, kx - kw * 0.5 - 0.05, 13.5, kz + 2.2, -Math.PI / 2, 1.1, 1);
  buildArchedWindow(ctx, kx + kw * 0.5 + 0.05, 13.5, kz, Math.PI / 2, 1.1, 1);

  // Forebuilding projection on the front of the Keep (with small pitched blue rooflet)
  ctx.addBox(5.2, 7.8, 2.2, kx, 7.9, kz - kd * 0.5 - 0.9, stoneMat, 1);
  const fbRoof = new THREE.ConeGeometry(3.4, 2.3, 4);
  const mFbRoof = new THREE.Matrix4()
    .makeTranslation(kx, 12.8, kz - kd * 0.5 - 0.9)
    .multiply(new THREE.Matrix4().makeScale(1.1, 1, 0.65))
    .multiply(new THREE.Matrix4().makeRotationY(Math.PI / 4));
  ctx.addGeo(fbRoof, roofBaseMat, 1, mFbRoof);
  ctx.addBox(0.22, 2.6, 0.28, kx - 1.1, 12.7, kz - kd * 0.5 - 1.95, goldMat, 1, 0, 0, -0.58);
  ctx.addBox(0.22, 2.6, 0.28, kx + 1.1, 12.7, kz - kd * 0.5 - 1.95, goldMat, 1, 0, 0, 0.58);

  // Upper Clerestory Extension of the Keep (Tiers 2 & 3)
  const upperBaseY = 16.0;
  const upperH = 5.5; // reaches y = 21.5
  ctx.addBox(kw, upperH, kd, kx, upperBaseY + upperH * 0.5, kz, stoneMat, 2);
  ctx.addGoldRim(kw + 0.58, 0.42, kd + 0.58, kx, upperBaseY + upperH - 0.35, kz, 0.24, 2);

  // Upper corner pilasters & towering Gold Corner Spires
  for (const [dx, dz] of [[-pOff, -pOff], [pOff, -pOff], [pOff, pOff], [-pOff, pOff]]) {
    const px = kx + dx;
    const pz = kz + dz;
    ctx.addBox(1.15, upperH + 0.8, 1.15, px, upperBaseY + (upperH + 0.8) * 0.5, pz, stoneMat, 2);
    ctx.addBox(1.38, 0.4, 1.38, px, 22.1, pz, goldMat, 2);
    ctx.addBox(1.02, 1.05, 1.02, px, 22.8, pz, goldMat, 2);
    ctx.addCylinder(0.04, 0.72, 2.6, 4, px, 24.6, pz, goldMat, 2, Math.PI / 4);
  }

  // Tall Arched Windows on Upper Keep Storey
  buildArchedWindow(ctx, kx - 2.9, 18.2, kz - kd * 0.5 - 0.05, Math.PI, 1.05, 2);
  buildArchedWindow(ctx, kx + 2.9, 18.2, kz - kd * 0.5 - 0.05, Math.PI, 1.05, 2);
  buildArchedWindow(ctx, kx - kw * 0.5 - 0.05, 18.2, kz - 2.2, -Math.PI / 2, 1.15, 2);
  buildArchedWindow(ctx, kx - kw * 0.5 - 0.05, 18.2, kz + 2.2, -Math.PI / 2, 1.15, 2);
  buildArchedWindow(ctx, kx + kw * 0.5 + 0.05, 18.2, kz - 2.2, Math.PI / 2, 1.15, 2);
  buildArchedWindow(ctx, kx + kw * 0.5 + 0.05, 18.2, kz + 2.2, Math.PI / 2, 1.15, 2);

  // Central Front Banner Strip & Glowing Keep Rune (Item 7 on plan: faces front -Z)
  buildHangingBanner(ctx, kx, 20.6, kz - kd * 0.5 - 0.28, 0, 2.35, 8.6, 1);
  buildRune(ctx, kx, 16.4, kz - kd * 0.5 - 0.45, 1.18, 1);

  // Grand Steep Blue-Slate Pyramid Roof of the Keep (y = 21.5 to 30.8, plus tall flagpole)
  buildRoof(ctx, kx, 21.5, kz, kw - 0.3, 9.2, kd - 0.3, {
    tier: 1,
    hasFlag: true,
    flagScale: 1.55,
    slateRows: 13,
    cornerSpires: false
  });

  // 4 Gabled Dormer Windows on the Keep Roof (Tier 3 full detail)
  buildDormer(ctx, kx + 1.5, 24.4, kz - 3.65, Math.PI, 1.05, 3);
  buildDormer(ctx, kx - 3.65, 24.4, kz - 1.2, -Math.PI / 2, 1.05, 3);
  buildDormer(ctx, kx + 3.65, 24.4, kz - 1.2, Math.PI / 2, 1.05, 3);
  buildDormer(ctx, kx, 24.4, kz + 3.65, 0, 1.05, 3);

  // --- Item 4: Blue-Roofed Gallery Bridge connecting Keep (-X face) to Right Colonnade (-X) ---
  const bridgeX = -5.4;
  const bridgeZ = 4.6;
  const bridgeW = 7.6;
  const bridgeD = 3.2;
  const bridgeRoofH = 1.65;

  ctx.addBox(bridgeW, 3.4, bridgeD, bridgeX, 11.8, bridgeZ, stoneMat, 2);
  ctx.addGoldRim(bridgeW + 0.16, 0.24, bridgeD + 0.28, bridgeX, 13.55, bridgeZ, 0.18, 2);
  buildArchedWindow(ctx, bridgeX + 1.2, 11.8, bridgeZ - bridgeD * 0.5 - 0.05, Math.PI, 0.82, 2);

  // Solid triangular prism core for the gallery gable roof
  const prismShape = new THREE.Shape();
  prismShape.moveTo(-bridgeD * 0.5, 0);
  prismShape.lineTo(bridgeD * 0.5, 0);
  prismShape.lineTo(0, bridgeRoofH);
  prismShape.closePath();
  const prismGeo = new THREE.ExtrudeGeometry(prismShape, {
    depth: bridgeW,
    bevelEnabled: false
  });
  prismGeo.center();
  const mPrism = new THREE.Matrix4()
    .makeTranslation(bridgeX, 13.65 + bridgeRoofH * 0.5, bridgeZ)
    .multiply(new THREE.Matrix4().makeRotationY(Math.PI / 2));
  ctx.addGeo(prismGeo, roofBaseMat, 2, mPrism);

  // Flush overlapping slate shingles on the gallery bridge roof
  const roofSteps = 5;
  const dummy = new THREE.Object3D();
  dummy.rotation.order = 'YXZ';
  const slateShades = [0xc2d4ff, 0x9eb8f5, 0xd4e2ff];
  const gSlope = Math.atan2(bridgeD * 0.5, bridgeRoofH);

  for (let r = 0; r < roofSteps; r++) {
    const v = (r + 0.45) / roofSteps;
    const ry = 13.68 + v * bridgeRoofH;
    const rzOff = (bridgeD * 0.5) * (1 - v) + 0.04;
    for (let c = 0; c < 9; c++) {
      const rx = bridgeX - bridgeW * 0.44 + c * (bridgeW * 0.88 / 8);
      // Front (-Z) slope
      dummy.position.set(rx, ry, bridgeZ - rzOff);
      dummy.rotation.set(-(gSlope - 0.04), Math.PI, 0);
      dummy.scale.set(0.92, 0.82, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + c) % slateShades.length], 2);
      // Back (+Z) slope
      dummy.position.set(rx, ry, bridgeZ + rzOff);
      dummy.rotation.set(-(gSlope - 0.04), 0, 0);
      dummy.scale.set(0.92, 0.82, 1.0);
      dummy.updateMatrix();
      ctx.addRoofSlateInstance(dummy.matrix, slateShades[(r + c + 1) % slateShades.length], 2);
    }
  }
  // Thin gold ridge bar on gallery bridge roof
  ctx.addBox(bridgeW + 0.2, 0.20, 0.22, bridgeX, 13.68 + bridgeRoofH + 0.05, bridgeZ, goldMat, 2);
}

/**
 * Builds the Front Gatehouse (Item 8 on plan): flanked by two crenellated stone pillars
 * standing on either side of the top staircase landing, with tiered gold spires, hanging
 * blue banners, a central gable with the second glowing teal diamond rune, and interactive doors.
 */
function buildGate(ctx, sceneGroup) {
  const { stoneMat, darkStoneMat, goldMat, woodMat, ironMat, windowGlowMat } = ctx.materials;
  const gz = -13.4;

  // Central Gatehouse Arch & Gable Block
  ctx.addBox(6.8, 8.2, 2.8, 0, 8.1, gz, stoneMat, 1);

  // Molded stone archway ring around the wooden doors
  const archR = 2.45;
  const archSegs = 13;
  for (let i = 0; i < archSegs; i++) {
    const theta = Math.PI * ((i + 0.5) / archSegs);
    const ax = Math.cos(theta) * archR;
    const ay = 6.6 + Math.sin(theta) * archR;
    ctx.addBox(0.62, 0.55, 0.65, ax, ay, gz - 1.25, stoneMat, 1, 0, 0, theta - Math.PI / 2);
  }
  ctx.addBox(0.65, 3.0, 0.65, -archR, 5.3, gz - 1.25, stoneMat, 1);
  ctx.addBox(0.65, 3.0, 0.65, archR, 5.3, gz - 1.25, stoneMat, 1);

  // Pointed Gold Gable Crown above the gate
  ctx.addBox(0.42, 5.2, 0.62, -1.75, 12.6, gz - 1.25, goldMat, 1, 0, 0, -0.78);
  ctx.addBox(0.42, 5.2, 0.62, 1.75, 12.6, gz - 1.25, goldMat, 1, 0, 0, 0.78);

  // Second Glowing Teal Diamond Rune above the Gate (Item 8 on plan)
  buildRune(ctx, 0, 11.4, gz - 1.48, 1.12, 1);

  // Two Flanking Gate Pillars standing directly on either side of the top staircase landing
  for (const side of [-1, 1]) {
    const px = side * 4.35;
    const pz = -14.3;
    ctx.addBox(2.6, 8.4, 2.7, px, 8.2, pz, stoneMat, 1);
    ctx.addGoldRim(2.78, 0.36, 2.88, px, 11.9, pz, 0.2, 1);
    ctx.addGoldRim(2.78, 0.28, 2.88, px, 9.6, pz, 0.18, 1);

    // Tiered Gold Spire atop each gate pillar
    ctx.addBox(1.85, 1.0, 1.85, px, 12.8, pz, goldMat, 1);
    ctx.addCylinder(0.75, 1.25, 1.1, 4, px, 13.7, pz, goldMat, 1, Math.PI / 4);
    ctx.addCylinder(0.04, 0.85, 2.5, 4, px, 15.3, pz, goldMat, 1, Math.PI / 4);

    // Hanging blue banner with gold diamond emblem
    buildHangingBanner(ctx, px, 9.45, pz - 1.48, 0, 1.48, 4.2, 1);

    // Gate Lantern Bracket & Flickering Candle PointLight
    const lx = side * 2.95;
    const ly = 6.9;
    const lz = gz - 1.65;
    ctx.addBox(0.16, 0.16, 0.55, lx, ly + 0.25, lz + 0.2, ironMat, 1);
    ctx.addBox(0.38, 0.52, 0.38, lx, ly, lz, windowGlowMat, 1);
    ctx.addCylinder(0.04, 0.34, 0.28, 4, lx, ly + 0.36, lz, ironMat, 1, Math.PI / 4);

    const lanternLight = new THREE.PointLight(PALETTE.windowLight, 3.2, 10);
    lanternLight.position.set(lx, ly, lz - 0.35);
    ctx.pointLights.push({
      light: lanternLight,
      type: 'lantern',
      baseIntensity: 3.2,
      phase: side * 1.7
    });
  }

  // Dark interior gate tunnel recess
  ctx.addBox(4.6, 5.2, 1.6, 0, 6.6, gz + 0.35, darkStoneMat, 1);

  // --- Interactive Double Wooden Doors (mounted on left & right hinge pivots) ---
  const doorPivots = [];
  const doorWidth = 2.15;
  const doorHeight = 4.75;

  // Helper to convert mixed indexed/non-indexed geometries cleanly before merging
  const normalizeAndMerge = (geoList) => {
    const clean = geoList.map((g) => {
      const ni = g.index ? g.toNonIndexed() : g.clone();
      if (!ni.attributes.uv) {
        const count = ni.attributes.position.count;
        ni.setAttribute('uv', new THREE.BufferAttribute(new Float32Array(count * 2), 2));
      }
      for (const k of Object.keys(ni.attributes)) {
        if (k !== 'position' && k !== 'normal' && k !== 'uv') ni.deleteAttribute(k);
      }
      return ni;
    });
    return BufferGeometryUtils.mergeGeometries(clean, false);
  };

  for (const side of [-1, 1]) {
    const pivot = new THREE.Group();
    pivot.position.set(side * doorWidth, 4.05, gz - 0.98);
    sceneGroup.add(pivot);

    const doorGeosWood = [];
    const doorGeosIron = [];
    const doorGeosGold = [];

    const numPlanks = 5;
    const plankW = doorWidth / numPlanks;
    for (let p = 0; p < numPlanks; p++) {
      const distFromHinge = (p + 0.5) * plankW;
      const distFromCenter = doorWidth - distFromHinge;
      const archDrop = Math.max(0, archR - Math.sqrt(Math.max(0, archR * archR - distFromCenter * distFromCenter)));
      const pH = doorHeight - archDrop * 0.82;

      const pGeo = new THREE.BoxGeometry(plankW - 0.03, pH, 0.28);
      pGeo.translate(-side * distFromHinge, pH * 0.5, 0);
      doorGeosWood.push(pGeo);
    }

    for (const strapY of [0.85, 2.35, 3.55]) {
      const sGeo = new THREE.BoxGeometry(doorWidth - 0.12, 0.18, 0.34);
      sGeo.translate(-side * (doorWidth * 0.5), strapY, 0);
      doorGeosIron.push(sGeo);

      const hGeo = new THREE.CylinderGeometry(0.12, 0.12, 0.38, 8);
      hGeo.translate(0, strapY, -0.12);
      doorGeosIron.push(hGeo);

      for (let st = 1; st <= 4; st++) {
        const studGeo = new THREE.OctahedronGeometry(0.08, 1);
        studGeo.translate(-side * (st * 0.42), strapY, -0.18);
        doorGeosIron.push(studGeo);
      }
    }

    const seamGeo = new THREE.BoxGeometry(0.14, doorHeight - 0.15, 0.35);
    seamGeo.translate(-side * (doorWidth - 0.07), (doorHeight - 0.15) * 0.5, 0);
    doorGeosGold.push(seamGeo);

    const bossShape = new THREE.Shape();
    bossShape.moveTo(0, 0.72);
    bossShape.lineTo(side * 0.72, 0);
    bossShape.lineTo(0, -0.72);
    bossShape.closePath();
    const bossGeo = new THREE.ExtrudeGeometry(bossShape, {
      depth: 0.22,
      bevelEnabled: true,
      bevelSegments: 1,
      bevelSize: 0.04,
      bevelThickness: 0.04
    });
    bossGeo.translate(-side * doorWidth, 1.85, -0.22);
    doorGeosGold.push(bossGeo);

    const woodMesh = new THREE.Mesh(normalizeAndMerge(doorGeosWood), woodMat);
    const ironMesh = new THREE.Mesh(normalizeAndMerge(doorGeosIron), ironMat);
    const goldMesh = new THREE.Mesh(normalizeAndMerge(doorGeosGold), goldMat);
    woodMesh.castShadow = true;
    woodMesh.receiveShadow = true;
    pivot.add(woodMesh, ironMesh, goldMesh);

    doorPivots.push({ pivot, side });
  }

  return doorPivots;
}

/**
 * Populates 2-sided leaf quad ivy strands hanging vertically down walls and trailing
 * across the lower plinth ledges.
 */
function buildFoliage(ctx) {
  const dummy = new THREE.Object3D();
  dummy.rotation.order = 'YXZ';
  const mossShades = [0x6e9430, 0x5c7e26, 0x7da43a, 0x4b691c, 0x87b041];

  // Hang vertical ivy strands flat against a wall face (rotY = outward normal yaw)
  function addWallIvyStrands(wallX, topY, wallZ, rotY, numStrands, strandLen, spanWidth, tier = 1) {
    const cosY = Math.cos(rotY);
    const sinY = Math.sin(rotY);

    for (let s = 0; s < numStrands; s++) {
      const lateral = (s / Math.max(1, numStrands - 1) - 0.5) * spanWidth + (rand() - 0.5) * 0.35;
      const nodes = Math.floor(strandLen * 3.5);
      for (let n = 0; n < nodes; n++) {
        const frac = n / nodes;
        const dropY = topY - frac * strandLen;
        const wave = Math.sin(frac * 8.0 + s * 1.7) * 0.14;
        const offsetOut = 0.08 + rand() * 0.05;

        const px = wallX + cosY * (lateral + wave) + sinY * offsetOut;
        const pz = wallZ - sinY * (lateral + wave) + cosY * offsetOut;

        dummy.position.set(px, dropY, pz);
        dummy.rotation.set(
          0.12 + (rand() - 0.5) * 0.2,
          rotY + (rand() - 0.5) * 0.25,
          (rand() - 0.5) * 0.35
        );
        const sc = (1.0 - frac * 0.35) * (0.75 + rand() * 0.45);
        dummy.scale.set(sc, sc, sc);
        dummy.updateMatrix();
        ctx.addMossInstance(dummy.matrix, mossShades[Math.floor(rand() * mossShades.length)], tier);
      }
    }
  }

  // Drape low-lying moss/ivy mats across stepped stone plinth ledges
  function addLedgeMossPatch(cx, cy, cz, rotY, spanW, count, tier = 1) {
    const cosY = Math.cos(rotY);
    const sinY = Math.sin(rotY);
    for (let i = 0; i < count; i++) {
      const lat = (rand() - 0.5) * spanW;
      const out = (rand() - 0.5) * 0.55;
      const px = cx + cosY * lat + sinY * out;
      const pz = cz - sinY * lat + cosY * out;
      dummy.position.set(px, cy + 0.06, pz);
      // Pitch forward so leaves drape over the step edge
      dummy.rotation.set(-0.65 + (rand() - 0.5) * 0.4, rotY + (rand() - 0.5) * 0.6, (rand() - 0.5) * 0.5);
      const sc = 0.75 + rand() * 0.55;
      dummy.scale.set(sc, sc, sc);
      dummy.updateMatrix();
      ctx.addMossInstance(dummy.matrix, mossShades[Math.floor(rand() * mossShades.length)], tier);
    }
  }

  // Front curtain walls & gate pillars (-Z face -> rotY = Math.PI)
  addWallIvyStrands(-10.5, 9.6, -14.08, Math.PI, 4, 4.2, 2.2, 1);
  addWallIvyStrands(-6.1, 9.6, -14.08, Math.PI, 4, 4.5, 1.8, 1);
  addWallIvyStrands(6.1, 9.6, -14.08, Math.PI, 4, 4.5, 1.8, 1);
  addWallIvyStrands(10.5, 9.6, -14.08, Math.PI, 4, 4.2, 2.2, 1);
  addWallIvyStrands(-15.7, 9.2, -12.0, -Math.PI / 2, 4, 4.0, 2.4, 1);
  addWallIvyStrands(15.7, 9.2, -12.0, Math.PI / 2, 4, 4.0, 2.4, 1);

  // Colonnade side (-X face -> rotY = -Math.PI / 2)
  addWallIvyStrands(-14.1, 9.5, -8.8, -Math.PI / 2, 4, 4.2, 2.2, 1);
  addWallIvyStrands(-14.1, 9.5, 8.8, -Math.PI / 2, 4, 4.2, 2.2, 1);

  // Left wall side (+X face -> rotY = +Math.PI / 2)
  addWallIvyStrands(14.1, 9.5, -6.5, Math.PI / 2, 4, 4.2, 2.4, 1);
  addWallIvyStrands(14.1, 9.5, 6.5, Math.PI / 2, 4, 4.2, 2.4, 1);

  // Central keep lower walls & gallery junction (matching Keep_3.png)
  addWallIvyStrands(-1.85, 14.5, 0.8, -Math.PI / 2, 5, 5.2, 2.8, 1);
  addWallIvyStrands(0.8, 12.2, -2.05, Math.PI, 4, 4.0, 2.2, 1);

  // Plinth ledges & stair flanks
  addLedgeMossPatch(-9.2, 2.2, -16.8, Math.PI, 5.5, 32, 1);
  addLedgeMossPatch(9.2, 2.2, -16.8, Math.PI, 5.5, 32, 1);
  addLedgeMossPatch(-9.2, 4.05, -15.3, Math.PI, 5.2, 28, 1);
  addLedgeMossPatch(9.2, 4.05, -15.3, Math.PI, 5.2, 28, 1);
  addLedgeMossPatch(-16.8, 2.2, -6.0, -Math.PI / 2, 6.0, 32, 1);
  addLedgeMossPatch(-16.8, 2.2, 6.0, -Math.PI / 2, 6.0, 32, 1);
  addLedgeMossPatch(16.8, 2.2, -6.0, Math.PI / 2, 6.0, 28, 1);
  addLedgeMossPatch(16.8, 2.2, 6.0, Math.PI / 2, 6.0, 28, 1);
  addLedgeMossPatch(0.0, 2.2, 16.8, 0, 12.0, 40, 1);
}

// ============================================================================
// 6. SCENE INITIALIZATION & FULL KEEP ASSEMBLY
// ============================================================================
const container = document.getElementById('canvas-container');
const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 0.90; // Lowered exposure so stone stays rich medium grey
renderer.info.autoReset = false;     // Manual reset so stats panel reads real numbers
container.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x181e2c);

// RoomEnvironment IBL
const pmremGenerator = new THREE.PMREMGenerator(renderer);
scene.environment = pmremGenerator.fromScene(new RoomEnvironment(), 0.04).texture;
scene.environmentIntensity = 0.52;

// Default Camera at (-36, 33, -42): Gate on left half of screen, -X colonnade on right half
const camera = new THREE.PerspectiveCamera(36, window.innerWidth / window.innerHeight, 0.5, 250);
camera.position.set(-36, 33, -42);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 11.5, 0);
controls.enableDamping = true;
controls.dampingFactor = 0.06;
controls.maxPolarAngle = Math.PI * 0.485;
controls.minDistance = 16;
controls.maxDistance = 110;
controls.update();

// --- Lighting Setup ---
const hemiLight = new THREE.HemisphereLight(0xdde8ff, 0x252b38, 0.68);
scene.add(hemiLight);

const sunLight = new THREE.DirectionalLight(0xfff1d6, 2.5);
sunLight.position.set(-34, 48, -32);
sunLight.castShadow = true;
sunLight.shadow.mapSize.set(2048, 2048);
sunLight.shadow.camera.left = -28;
sunLight.shadow.camera.right = 28;
sunLight.shadow.camera.top = 34;
sunLight.shadow.camera.bottom = -24;
sunLight.shadow.camera.near = 10;
sunLight.shadow.camera.far = 110;
sunLight.shadow.bias = -0.0005;
scene.add(sunLight);

const fillLight = new THREE.DirectionalLight(0x7ca2ff, 0.75);
fillLight.position.set(30, 20, 28);
scene.add(fillLight);

// Soft Ground Disc beneath the Keep
const groundGeo = new THREE.CylinderGeometry(42, 44, 0.6, 64);
const groundMat = new THREE.MeshStandardMaterial({
  color: 0x1d2330,
  roughness: 0.94,
  metalness: 0.04
});
const groundMesh = new THREE.Mesh(groundGeo, groundMat);
groundMesh.position.y = -0.3;
groundMesh.receiveShadow = true;
scene.add(groundMesh);

// --- Build the Entire Keep Hierarchy ---
const materials = createProceduralMaterials();
const builderCtx = new KeepBuilderContext(materials);
const keepRoot = new THREE.Group();
keepRoot.name = 'FantasyKeep';
scene.add(keepRoot);

// 1. Plinth & Front Stairs (-Z only)
buildPlinth(builderCtx);

// 2. Curtain Walls & Walkways (+X stepped wall-walk, +Z postern door)
buildWalls(builderCtx);

// 3. Right-Side (-X) Two-Storey Arched Colonnade (Tier 2+)
buildColonnade(builderCtx);

// 4. Four Corner Towers + One Back-Wall Tower (matching plan items 1 & 2)
// Front-Right Corner Tower (-13, -13, closest to camera in Keep_3.png) — Tier 1
buildTower(builderCtx, -13.0, -13.0, {
  tier: 1,
  bannerSides: ['front', 'right'],
  windowSides: ['front', 'right'],
  hasFlag: false
});
// Front-Left Corner Tower (+13, -13, on left of screen in Keep_3.png) — Tier 1
buildTower(builderCtx, 13.0, -13.0, {
  tier: 1,
  bannerSides: ['front', 'left'],
  windowSides: ['front', 'left'],
  hasFlag: true
});
// Back-Right Corner Tower (-13, +13, at far right of screen in Keep_3.png) — Tier 2
buildTower(builderCtx, -13.0, 13.0, {
  tier: 2,
  height: 11.2,
  bannerSides: ['right'],
  windowSides: ['right', 'back'],
  hasFlag: true
});
// Back-Left Corner Tower (+13, +13, behind keep on left of screen) — Tier 2
buildTower(builderCtx, 13.0, 13.0, {
  tier: 2,
  height: 12.0,
  bannerSides: ['left'],
  windowSides: ['left', 'back'],
  hasFlag: true
});
// Back-Wall Tower (-3.2, +13.0, Item 2 on plan) — Tier 3
buildTower(builderCtx, -3.2, 13.0, {
  width: 4.8,
  height: 12.6,
  roofHeight: 5.2,
  tier: 3,
  bannerSides: ['right'],
  windowSides: ['back'],
  hasFlag: false
});

// 5. Central Keep (+3.2, +3.0) & Gallery Roof (-5.4, +4.6) (Items 3, 4, 7)
buildKeep(builderCtx);

// 6. Front Gatehouse, Rune & Interactive Doors (Item 8)
const doorPivots = buildGate(builderCtx, keepRoot);

// 7. Creeping 2-Sided Leaf Quad Ivy Strands & Moss
buildFoliage(builderCtx);

// Attach PointLights to keepRoot
for (const pl of builderCtx.pointLights) {
  keepRoot.add(pl.light);
}

// --- Merge Static Geometries & Build InstancedMeshes Per Upgrade Tier (1, 2, 3) ---
const tierGroups = {
  1: new THREE.Group(),
  2: new THREE.Group(),
  3: new THREE.Group()
};
tierGroups[1].name = 'Tier1_Core';
tierGroups[2].name = 'Tier2_Expansion';
tierGroups[3].name = 'Tier3_Masterwork';
keepRoot.add(tierGroups[1], tierGroups[2], tierGroups[3]);

const blockProtoGeo = createBevelledBlockGeometry();
const slateProtoGeo = createRoofSlateGeometry();
const ivyProtoGeo = createIvyClusterGeometry();

for (const t of [1, 2, 3]) {
  const group = tierGroups[t];
  const matMap = builderCtx.buckets[t];

  for (const [mat, geoList] of matMap.entries()) {
    if (geoList.length === 0) continue;
    const merged = BufferGeometryUtils.mergeGeometries(geoList, false);
    const mesh = new THREE.Mesh(merged, mat);
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    group.add(mesh);
  }

  // Instanced Stone Blocks
  const blocks = builderCtx.instances[t].stoneBlocks;
  if (blocks.length > 0) {
    const inst = new THREE.InstancedMesh(blockProtoGeo, materials.stoneBlockMat, blocks.length);
    inst.castShadow = true;
    inst.receiveShadow = true;
    const c = new THREE.Color();
    blocks.forEach((item, idx) => {
      inst.setMatrixAt(idx, item.matrix);
      inst.setColorAt(idx, c.setHex(item.colorHex));
    });
    inst.instanceMatrix.needsUpdate = true;
    inst.instanceColor.needsUpdate = true;
    group.add(inst);
  }

  // Instanced Flush Roof Slates
  const slates = builderCtx.instances[t].roofSlates;
  if (slates.length > 0) {
    const inst = new THREE.InstancedMesh(slateProtoGeo, materials.roofTileMat, slates.length);
    inst.castShadow = true;
    inst.receiveShadow = true;
    const c = new THREE.Color();
    slates.forEach((item, idx) => {
      inst.setMatrixAt(idx, item.matrix);
      inst.setColorAt(idx, c.setHex(item.colorHex));
    });
    inst.instanceMatrix.needsUpdate = true;
    inst.instanceColor.needsUpdate = true;
    group.add(inst);
  }

  // Instanced 2-Sided Leaf Quad Ivy Strands
  const leaves = builderCtx.instances[t].mossLeaves;
  if (leaves.length > 0) {
    const inst = new THREE.InstancedMesh(ivyProtoGeo, materials.mossMat, leaves.length);
    inst.castShadow = true;
    inst.receiveShadow = true;
    const c = new THREE.Color();
    leaves.forEach((item, idx) => {
      inst.setMatrixAt(idx, item.matrix);
      inst.setColorAt(idx, c.setHex(item.colorHex));
    });
    inst.instanceMatrix.needsUpdate = true;
    inst.instanceColor.needsUpdate = true;
    group.add(inst);
  }
}

// ============================================================================
// 7. INTERACTION, UPGRADE TIERS, DAY/NIGHT & GLTF EXPORT
// ============================================================================
let isGateOpen = false;
let gateAnimProgress = 0;
let isNightMode = false;
let currentTier = 3;

const statTrisEl = document.getElementById('stat-tris');
const statCallsEl = document.getElementById('stat-calls');
const statFpsEl = document.getElementById('stat-fps');
const statGateEl = document.getElementById('stat-gate');

function toggleGate() {
  isGateOpen = !isGateOpen;
  statGateEl.textContent = isGateOpen ? 'Open' : 'Closed';
  statGateEl.style.color = isGateOpen ? '#8CE8DC' : '#FFFFFF';
}

const raycaster = new THREE.Raycaster();
const pointer = new THREE.Vector2();
let pointerDownPos = { x: 0, y: 0 };

renderer.domElement.addEventListener('pointerdown', (e) => {
  pointerDownPos = { x: e.clientX, y: e.clientY };
});

renderer.domElement.addEventListener('pointerup', (e) => {
  const dx = e.clientX - pointerDownPos.x;
  const dy = e.clientY - pointerDownPos.y;
  if (Math.hypot(dx, dy) > 6) return;

  pointer.x = (e.clientX / window.innerWidth) * 2 - 1;
  pointer.y = -(e.clientY / window.innerHeight) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);

  const doorMeshes = [];
  doorPivots.forEach((dp) => dp.pivot.traverse((ch) => { if (ch.isMesh) doorMeshes.push(ch); }));
  const hits = raycaster.intersectObjects(doorMeshes, false);
  if (hits.length > 0) {
    toggleGate();
  }
});

// UI Button Bindings
const btnDay = document.getElementById('btn-day');
const btnNight = document.getElementById('btn-night');
const btnTier1 = document.getElementById('btn-tier1');
const btnTier2 = document.getElementById('btn-tier2');
const btnTier3 = document.getElementById('btn-tier3');
const btnRotate = document.getElementById('btn-rotate');
const btnGate = document.getElementById('btn-gate');
const btnExport = document.getElementById('btn-export');

function setLightingMode(night) {
  isNightMode = night;
  btnDay.classList.toggle('active', !night);
  btnNight.classList.toggle('active', night);

  if (night) {
    scene.background.setHex(0x090d16);
    scene.environmentIntensity = 0.16;
    sunLight.intensity = 0.22;
    sunLight.color.setHex(0x6b8cff);
    hemiLight.intensity = 0.20;
    fillLight.intensity = 0.22;
  } else {
    scene.background.setHex(0x181e2c);
    scene.environmentIntensity = 0.52;
    sunLight.intensity = 2.5;
    sunLight.color.setHex(0xfff1d6);
    hemiLight.intensity = 0.68;
    fillLight.intensity = 0.75;
  }
}

function setUpgradeTier(tier) {
  currentTier = tier;
  btnTier1.classList.toggle('active', tier === 1);
  btnTier2.classList.toggle('active', tier === 2);
  btnTier3.classList.toggle('active', tier === 3);

  tierGroups[1].visible = true;
  tierGroups[2].visible = tier >= 2;
  tierGroups[3].visible = tier >= 3;
}

btnDay.addEventListener('click', () => setLightingMode(false));
btnNight.addEventListener('click', () => setLightingMode(true));
btnTier1.addEventListener('click', () => setUpgradeTier(1));
btnTier2.addEventListener('click', () => setUpgradeTier(2));
btnTier3.addEventListener('click', () => setUpgradeTier(3));
btnGate.addEventListener('click', toggleGate);

btnRotate.addEventListener('click', () => {
  controls.autoRotate = !controls.autoRotate;
  controls.autoRotateSpeed = 1.4;
  btnRotate.classList.toggle('active', controls.autoRotate);
  btnRotate.textContent = `Auto-Rotate: ${controls.autoRotate ? 'On' : 'Off'}`;
});

btnExport.addEventListener('click', () => {
  btnExport.textContent = 'Exporting...';
  const exporter = new GLTFExporter();
  exporter.parse(
    keepRoot,
    (glbBuffer) => {
      const blob = new Blob([glbBuffer], { type: 'model/gltf-binary' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `fantasy_keep_tier${currentTier}.glb`;
      a.click();
      URL.revokeObjectURL(url);
      btnExport.textContent = 'Export glTF (.glb)';
    },
    (err) => {
      console.error('GLTF export error:', err);
      btnExport.textContent = 'Export glTF (.glb)';
    },
    { binary: true }
  );
});

window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

// ============================================================================
// 8. ANIMATION LOOP & LIVE STATS PANEL
// ============================================================================
const clock = new THREE.Clock();
let frameCount = 0;
let fpsAccum = 0;

function easeInOutCubic(t) {
  return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
}

function animate() {
  requestAnimationFrame(animate);

  const dt = Math.min(clock.getDelta(), 0.1);
  const elapsed = clock.getElapsedTime();
  windUniforms.uTime.value = elapsed;

  // 1. Eased Gate Door Swing Animation
  const targetGate = isGateOpen ? 1 : 0;
  if (Math.abs(gateAnimProgress - targetGate) > 0.001) {
    gateAnimProgress += (targetGate - gateAnimProgress) * Math.min(1, dt * 4.5);
    const eased = easeInOutCubic(gateAnimProgress);
    const maxAngle = Math.PI * 0.48;
    for (const dp of doorPivots) {
      // Swing inward into the gatehouse archway
      dp.pivot.rotation.y = dp.side * eased * maxAngle;
    }
  }

  // 2. Emissive Breathing on Teal Runes & Candlelight Flicker on Windows/Lanterns
  const nightBoost = isNightMode ? 1.55 : 1.0;
  const runePulse = (0.82 + 0.28 * Math.sin(elapsed * 2.2)) * nightBoost;
  materials.runeOuterMat.emissiveIntensity = 2.0 * runePulse;
  materials.runeCoreMat.emissiveIntensity = 3.4 * runePulse;

  const candleFlicker =
    (0.92 +
      0.08 * Math.sin(elapsed * 13.7) +
      0.05 * Math.cos(elapsed * 23.1) +
      0.04 * Math.sin(elapsed * 37.3)) *
    nightBoost;
  materials.windowGlowMat.emissiveIntensity = 2.4 * candleFlicker;

  for (const pl of builderCtx.pointLights) {
    if (pl.type === 'rune') {
      pl.light.intensity =
        pl.baseIntensity * (0.85 + 0.25 * Math.sin(elapsed * 2.2 + pl.phase)) * nightBoost;
    } else {
      pl.light.intensity =
        pl.baseIntensity *
        (0.9 + 0.14 * Math.sin(elapsed * 15.0 + pl.phase) * Math.cos(elapsed * 27.0)) *
        nightBoost;
    }
  }

  controls.update();

  // 3. Reset Renderer Stats Before Render, Then Sample Live Counts After Render
  renderer.info.reset();
  renderer.render(scene, camera);

  const liveTris = renderer.info.render.triangles;
  const liveCalls = renderer.info.render.calls;

  frameCount++;
  fpsAccum += dt;
  if (fpsAccum >= 0.25) {
    const fps = Math.round(frameCount / fpsAccum);
    statFpsEl.textContent = `${fps} FPS`;
    statTrisEl.textContent = liveTris.toLocaleString();
    statCallsEl.textContent = liveCalls.toLocaleString();
    frameCount = 0;
    fpsAccum = 0;
  }
}

animate();
