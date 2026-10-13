# AI playtest

Run from the repository root with a temporary local Admin__Token environment variable (at least 24 characters): dotnet run --project tools/AiPlaytest.

The tool starts a fixed Dev/InMemory loopback host on 5090, advances its private ManualClock by thirty seconds per turn and waits for real server ticks. It sends only player-visible WebSocket intents. GM calls are GET-only exports. No production account, storage or GM grants are used.

Four profiles: quest (strict tutorial route), diligent (queues and army growth), random (seeded choices and rejected upgrades), offline (15 active minutes, 8 hours offline, 15 active minutes). Output is artifacts/ai-playtest. Run --host for a separate 10x-clock client session; use -devlogin -devname aiplay-ui -server ws://127.0.0.1:5090/ws -aiplaytest <absolute-capture-folder> on the Windows client.

Client automation invokes existing navigation/upgrade/expedition/feedback handlers and sends other public intents; it is not physical mouse/keyboard automation. The diagnostic captures major pages, reached quests, resource/building cards and error/feedback UI. Unreached tutorial steps are not invented.

LocalClock rejects Firebase and Supabase. The production GameServer's SystemClock is unchanged. No production time-control endpoint or environment setting is added. Stop the host after capture.

AI can identify reproducible failures and unclear paths. It cannot decide whether real players find the game fun. Findings are reported without fixing gameplay.
