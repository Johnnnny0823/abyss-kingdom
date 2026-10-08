# World model generation

Original low-poly Covenant Ruins art: slate blue masonry, turquoise contract seals,
warm brass details. No external assets, textures, add-ons, or reference artwork.

## Generate

Run from the repository root in PowerShell:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python tools/blender/generate_world.py -- --batch all
```

Use `--batch city`, `--batch terrain`, or `--batch landmarks` for incremental work.
Edit `PALETTE` and `SPECS` in the script for colors and final meter dimensions.
Geometry is deterministically regenerated; existing FBX files are replaced in place.
Keep their Unity `.meta` files so asset GUIDs remain stable. Before replacing existing
assets or making large edits, back them up following the repository/user policy.

Exports: `client/Assets/Resources/Models/World/*.fbx` (13 files, 11 categories).
Checks: `artifacts/models/*.json` (ignored local evidence).
Each export is one flat-shaded, triangulated mesh with 1–4 Base Color materials,
bottom-center origin and applied transforms. FBX uses selected Mesh objects only,
FBX Units Scale, -Z Forward / Y Up, Apply Transform and Apply Modifiers.

## Build and inspect

Close the Unity editor, then run:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.4.8f1/Editor/Unity.exe' -batchmode -quit -nographics -projectPath "$PWD/client" -executeMethod BuildScript.BuildWindows -logFile "$PWD/artifacts/models-build.log"
```

Start a separate local Dev server (no production credentials):

```powershell
$env:Auth__Mode = 'Dev'
$env:Storage__Mode = 'InMemory'
$env:Logging__EventLog__LogLevel__Default = 'None'
Remove-Item Env:Admin__Token -ErrorAction SilentlyContinue
dotnet run --project server/AbyssKingdom.Server --urls http://127.0.0.1:5090
```

Run the normal client with opt-in art capture:

```powershell
& ./client/Builds/Windows/AbyssKingdom.exe -devlogin -server ws://127.0.0.1:5090/ws -continent -artcapture "$PWD/artifacts/models-review" -logFile "$PWD/artifacts/models-player.log" -screen-width 1280 -screen-height 800 -screen-fullscreen 0
```

`WorldModelCapture` only starts with `-devlogin`, an explicit loopback server URL,
and `-artcapture`. It waits for a real Dev login, captures the world map and each
available imported model using `ModelLibrary.Spawn` at the production 0.2 tile
scale, checks bounds/grounding/triangle budgets/materials, then exits.
The log must contain `ART_CHECK COMPLETE models=13 failures=0` for the full set.
Inspect the PNGs for color, silhouette and front orientation; automatic checks do
not replace visual review. Missing models are allowed only for incremental batches.

For the rules regression suite, stop the Dev server first, clear the inherited
`Admin__Token` in the test shell, and use the same EventLog override before running
`dotnet run --project tests/AbyssKingdom.Tests`.
