# 給 AI 助手的專案說明

這是一人 + AI 開發的手機 4X SLG《深淵王國》。開發者主要透過 AI 寫程式（Vibe Coding），所以請遵守以下規則，讓專案長期可維護。

## 架構原則

- **伺服器權威**：所有數值、計時、戰鬥都在伺服器計算。客戶端只送「意圖」，只用共用邏輯做顯示預測。
- **共用規則放 `shared/`**：遊戲規則寫在 `shared/AbyssKingdom.Shared`，必須是純 C#（netstandard2.1、C# 9），不可引用 NuGet 套件或 UnityEngine，伺服器與 Unity 都編譯它。
- **確定性**：規則只用整數（long）與傳入的時間（Unix 毫秒），不可呼叫 `DateTime.Now`、亂數或浮點數。需要亂數時由伺服器傳入種子。
- **模組化單體**：伺服器目前是單一程式，依資料夾分模組（Auth / Game / Net / Persistence，之後加 Map / Battle / Social）。模組之間透過介面溝通，方便日後拆服務。
- **同一玩家的狀態修改必須持有 `PlayerSession.Gate`**。

## 開發流程

1. 先看 README 的進度與 docs/ 的規格，一次只做一個小功能。
2. 規則改動一定要在 `tests/AbyssKingdom.Tests` 加測試；跑 `dotnet run --project tests/AbyssKingdom.Tests` 全部 PASS 才算完成。
3. 新增訊息類型：常數加在 `shared/.../Protocol/Messages.cs`，伺服器在 `Net/WebSocketHandler.cs` 分派，README 協定表同步更新。
4. 測試專案不使用 NuGet（自帶 `[Test]` 迷你框架），請維持這個做法。

## 安全

- 秘密（Supabase service key、Firebase 服務帳號、資料庫密碼）只能放環境變數，絕不寫進程式碼、設定檔或客戶端。
- 正式環境 `Auth__Mode` 必須是 `Firebase`。

## 版權

世界觀只借用「垂直深淵探勘」的概念，不可使用《來自深淵》的名詞、角色、造型或劇情。美術與文案須原創。
