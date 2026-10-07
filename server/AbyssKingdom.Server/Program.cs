using AbyssKingdom.Server;

// 進入點：建立並啟動遊戲伺服器。
// 設定請看 appsettings.json，或用環境變數覆寫（例如 Auth__Mode=Firebase）。
var app = GameServer.Build(args);
app.Run();
