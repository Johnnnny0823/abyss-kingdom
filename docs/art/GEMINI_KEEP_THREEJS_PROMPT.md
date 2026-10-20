# Gemini 4 Argon 提示詞：用程式碼做出可互動的 3D 主堡（Three.js）

## 使用方式（Antigravity）

1. 在 Antigravity 開一個新的空資料夾當工作區（例如 `abyss-keep-3d/`），模型選 **Gemini 4 Argon**。
2. 附上三個檔案：
   - `client/Assets/Resources/Art/Buildings/Keep_3.png`（外觀參考，最重要）
   - `docs/art/ref/keep/keep_plan.svg`（平面圖）
   - `docs/art/ref/keep/front.webp`（正面細節）
3. 貼上下面「提示詞」整段（英文寫給模型，效果比較好）。
4. 做出來後用 Antigravity 內建瀏覽器或本機伺服器開 `index.html`。截圖或錄影給 Claude，可以再一輪一輪修。

> 版權：參考圖都是本專案原創的美術，可以放心餵給模型。不要另外餵別款遊戲的城堡圖。

## 提示詞

```
Build an interactive, highly detailed 3D model of the fantasy keep shown in the attached image
(Keep_3.png), generated entirely in code with Three.js. No external 3D models, no Blender,
no image textures downloaded from the internet: every mesh is built procedurally and every
texture is drawn procedurally (canvas or shader).

DELIVERABLE
- A single static web page: index.html + main.js (ES modules). Load Three.js from a CDN through
  an import map with an exact pinned version (e.g. three@0.170.0, plus its OrbitControls,
  RoomEnvironment and GLTFExporter addons from the same version). No build step, no npm.
- It must run by opening it from a simple local static server.

DESIGN (follow the attached plan; the image is the look, the plan is the layout)
- Square footprint on a stepped stone plinth with gold arrow-shaped buttresses at the corners.
- Curtain walls with crenellations and a gold trim band; 4 square corner towers and 1 back-wall
  tower, each with a steep blue slate pyramid roof, gold ridges, gold finials and a swallow-tail
  blue pennant.
- The central keep is the tallest element, offset to the LEFT and BACK of centre, with a steep
  blue roof, small dormer windows, gold corner spires and a large teal diamond rune on its front.
- Front: arched wooden double gate with iron straps, flanked by two pillars with gold spires and
  hanging blue banners (gold diamond emblem). A second teal diamond rune above the gate.
  A wide stone staircase in front of the gate. Stairs and gate exist ONLY on the front.
- Right side only: a two-storey arched colonnade facing an open inner courtyard, with a blue-roofed
  gallery connecting the keep to the right wall. Left side: a stepped wall-walk. Back: solid wall,
  at most one small postern door.
- Moss and ivy creeping on lower walls; warm lit windows.
- Palette: slate blue roofs #2F4FB0, gold trim #D8A93A, stone #8E8F96 with variation, wood #7A4B2A,
  rune glow #37BCAE / #B8FFF1, window light #F6B647.

GEOMETRY QUALITY
- Aim for 100k-200k triangles total. Use real geometry for detail: individual roof slate rows,
  bevelled stone blocks on the plinth, crenellation merlons, door planks and hinges, window
  frames, gold trims.
- Use InstancedMesh for repeated parts (bricks, slates, merlons, ivy leaves) and merge static
  geometry with BufferGeometryUtils so the draw call count stays low. Show triangle count and
  draw calls in a small on-screen stats panel.
- Organise the code into small builder functions (buildPlinth, buildWalls, buildTower, buildRoof,
  buildKeep, buildGate, buildColonnade, buildBanner, ...), each documented with a short comment.

INTERACTION AND ANIMATION
- OrbitControls with damping; a ground disc with soft shadows; ACES tone mapping; RoomEnvironment
  lighting plus a warm key light.
- Click the gate: the two doors swing open on their hinges (eased animation); click again to close.
- Banners and pennants wave with a vertex shader (fixed edge stays still, free edge moves most).
- Runes pulse slowly (emissive breathing); windows and two gate lanterns flicker like candlelight
  with small point lights.
- UI buttons: Day / Night (night dims the sun and lets the glow carry the scene), Upgrade tier
  1 / 2 / 3 (tier 1 = smaller keep without colonnade and with fewer towers; tier 3 = full design),
  Auto-rotate on/off, and "Export glTF" (downloads the current model as .glb via GLTFExporter).

CODE QUALITY
- Clean, readable, commented. Deterministic: use a seeded random function so the model looks
  identical on every load.
- Keep it smooth (60 fps on a mid-range laptop).

Work in steps: first the overall massing that matches the plan, then the roofs and towers,
then fine detail, then animation and UI. After each step, take a screenshot and compare it with
Keep_3.png; list what still differs and fix it before moving on.
```

## 做完之後

- **放進作品集**：可以發佈成網頁（例如 GitHub Pages），README 放動圖與「在線上看 3D 主堡」的連結。這個作品同時展示 JavaScript、HTML5 和 3D 圖學，對實習的「網頁開發類」加分。
- **放進遊戲**：用頁面上的「Export glTF」匯出 `.glb`。Unity 要讀 glTF 需要另外裝套件（例如 glTFast），而且遊戲的規格是 4,000～5,000 個三角形（`docs/art/MODEL_SPEC.md` 5b）。網頁版是「高細節展示版」，**不能直接拿來當遊戲模型**，要再減面。

## 第二輪：修正提示詞（2026-10-20，Claude 檢查第一版後）

第一版存在 `web/keep3d/`。Claude 已經先修了兩個讓畫面全白的錯誤（見提示詞第 1 點），其他照下面貼給 Gemini。

```
Thanks, this is a strong first version. I ran it; please fix the following (keep everything
else as is):

1. Two runtime bugs I already patched locally - keep these fixes:
   - buildDormer(): `gRight` was never defined (ReferenceError stopped the whole build).
     Reuse the gLeft geometry for the right bargeboard.
   - buildGate(): mergeGeometries failed because BoxGeometry/CylinderGeometry are indexed but
     OctahedronGeometry/ExtrudeGeometry are not. Convert every geometry with toNonIndexed()
     before merging.

2. The layout is mirrored. Standing in front of the gate and facing the keep, the colonnade and
   gallery must be on the viewer's RIGHT, which in this scene is the -X side (front is -Z).
   Negate the X of the whole layout (keep at +X/back, colonnade and gallery at -X), and move the
   default camera to (-36, 33, -42) so the gate is on the left half of the screen and the
   colonnade side is visible on the right half, like Keep_3.png.

3. Roof slates stick out like spikes and float off the roof. Each slate row must lie flush on the
   pyramid face it belongs to (rotate by the face's real slope, offset slightly along the face
   normal, rows overlapping like shingles). Same for the gallery roof.

4. Stone looks almost white. Darken the stone albedo to around #7E8089 with more variation
   between blocks, lower tone mapping exposure to about 0.9, and keep gold only as thin trims.

5. Remove the large flat gold slabs on top of the walls and the left wall-walk; wall tops and
   walkways are stone. Gold is for trims, finials, frames and banner rods only.

6. The ivy looks like green balls. Replace it with small leaf quads (2-sided planes, a few leaf
   shapes) clustered flat against the walls, hanging downward in strands.

7. The stats panel stays at 0 triangles / 0 draw calls. Read renderer.info right after
   renderer.render() each frame (or set renderer.info.autoReset = false and reset manually) so
   the numbers are real.

8. Make the front staircase clearly visible from the default camera (wider and slightly longer),
   with the two gate pillars standing on either side of its top landing.

After the fixes, take screenshots from the default camera, from the back, and from the right
side, compare with Keep_3.png and keep_plan.svg, and list anything that still differs.
```
