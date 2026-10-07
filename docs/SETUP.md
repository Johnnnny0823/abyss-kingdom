# 開發環境安裝（全部免費）

依序安裝，每一步都附「怎麼確認裝好了」。

## 1. Git

- Windows：到 https://git-scm.com 下載安裝，全部用預設值。
- macOS：打開「終端機」輸入 `git --version`，系統會提示安裝。
- 確認：終端機輸入 `git --version` 會顯示版本號。

另外建議安裝 **GitHub Desktop**（https://desktop.github.com），用滑鼠就能上傳程式碼。

## 2. .NET 8 SDK（伺服器與測試）

- 到 https://dotnet.microsoft.com/download/dotnet/8.0 下載 **SDK**（不是 Runtime）。
- 確認：終端機輸入 `dotnet --version`，顯示 `8.0.xxx`。

## 3. Unity Hub + Unity 6 LTS（客戶端）

1. 到 https://unity.com/download 安裝 Unity Hub，用免費的 **Personal** 授權登入。
2. Unity Hub → Installs → Install Editor → 選 **Unity 6 (6000.0 LTS)**。
3. 勾選模組：**Android Build Support**（含 OpenJDK、Android SDK & NDK）。之後要出 iOS 才需要 Mac。
4. 確認：Unity Hub 的 Installs 頁看得到 6000.0.x。

## 4. 程式編輯器

- **Visual Studio Code**（https://code.visualstudio.com）＋ C# Dev Kit 擴充套件，或
- **JetBrains Rider**（學生可申請免費授權）。

## 5. 第一次跑起來

```bash
# 下載專案（把網址換成你的 repo）
git clone https://github.com/你的帳號/abyss-kingdom.git
cd abyss-kingdom

# 跑測試：最後一行應顯示 "22 passed, 0 failed"
dotnet run --project tests/AbyssKingdom.Tests

# 啟動伺服器：看到 "Now listening on: http://0.0.0.0:5080" 就成功
dotnet run --project server/AbyssKingdom.Server
```

瀏覽器打開 http://127.0.0.1:5080/healthz，看到 `{"status":"ok","online":0}` 表示伺服器正常。

## 6. 開啟 Unity 客戶端

1. Unity Hub → Projects → Add → Add project from disk → 選專案裡的 `client` 資料夾。
2. 如果提示版本不同，選你安裝的 Unity 6 版本開啟即可。
3. 第一次開啟會匯入一陣子；完成後開一個空場景（File → New Scene）。
4. 確認伺服器已啟動，按 **Play**：左上角會出現「深淵王國 — 開發面板」。
5. 輸入名字 → 連線並登入 → 試著升級兵營，倒數結束後會收到「升級完成」。

> 開發面板用的是 Unity 內建 IMGUI，在編輯器裡中文可以正常顯示；手機上若出現方塊字，之後換成正式 UI 並放入中文字型即可。

## 常見問題

| 狀況 | 解法 |
| --- | --- |
| `dotnet` 找不到指令 | 重新開啟終端機；仍不行就重新安裝 .NET 8 SDK |
| Unity 顯示找不到 `com.abysskingdom.shared` | 確認 `client` 與 `shared` 在同一個專案根目錄下，沒有被單獨搬走 |
| 連線失敗 | 先確認伺服器有在跑、網址是 `ws://127.0.0.1:5080/ws` |
| 手機連不到電腦上的伺服器 | 手機與電腦需在同一個 Wi-Fi，網址改成電腦的區網 IP（例如 `ws://192.168.1.10:5080/ws`），並允許防火牆 |
