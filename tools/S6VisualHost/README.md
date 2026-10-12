# Local S6 visual fixture

Run from the repository root:

```powershell
dotnet run --project tools/S6VisualHost/S6VisualHost.csproj
```

The host binds only `127.0.0.1:5090`, uses Dev authentication, memory stores and a fixed manual clock. GM is disabled. It creates four dedicated preview accounts (self, ally, foreign alliance, unaffiliated), a paid completed flag and a two-player mixed rally with 600 troops. It never uses production accounts or storage.

Start the Windows client with `-devlogin -devname S6PreviewLeader -alliance -server ws://127.0.0.1:5090/ws -artcapture <absolute-output-directory> -artcapture-offscreen -logFile <absolute-log-path>`.

Captures render the actual scene camera without the UI overlay. They verify scene composition and imported models, not manual UI interactions. The capture client quits itself; stop this host before tests or rebuilding server assemblies.
