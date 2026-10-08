# 部署：本機 Docker + Cloudflare Quick Tunnel

目前（封測前）的部署方式：伺服器跑在開發者自己的 Windows 電腦上，用 Cloudflare Quick Tunnel 對外開放。
免費、免 Cloudflare 帳號、免網域，而且自動提供 `wss://`（TLS），Android 不會被「禁止明文連線」擋住。

代價：**電腦要開著、Docker 要在跑**，朋友才連得上；而且 **每次重啟 tunnel，網址都會變**。
之後換到雲端主機（Oracle 等）時，同一份 `docker-compose.yml` 直接沿用。

## 一次性準備

1. 安裝並開啟 Docker Desktop。
2. Windows 使用者環境變數設定（「編輯您帳戶的環境變數」）：
   - `Supabase__Url` = `https://<專案代號>.supabase.co`（不是 dashboard 網址）
   - `Supabase__ServiceKey` = Supabase 的 service_role key
   - 設好後要**開新的** PowerShell 視窗才讀得到。

## 啟動

在專案根目錄開 PowerShell：

```powershell
docker compose up -d --build
docker compose logs tunnel | Select-String trycloudflare.com
```

第二行會印出像 `https://xxxx-xxxx-xxxx.trycloudflare.com` 的網址。給玩家的伺服器網址是把 `https` 換成 `wss`、結尾加 `/ws`：

```
wss://xxxx-xxxx-xxxx.trycloudflare.com/ws
```

在 Unity 開發面板的「伺服器網址」欄位貼上即可。

檢查：瀏覽器打開 `https://xxxx-xxxx-xxxx.trycloudflare.com/healthz`，應看到 `{"status":"ok",...}`。

## 日常操作

| 要做的事 | 指令 |
| --- | --- |
| 看狀態 | `docker compose ps` |
| 看伺服器 log | `docker compose logs -f server` |
| 改了程式碼要更新 | `docker compose up -d --build server`（只重建伺服器，tunnel 網址不變） |
| 停止 | `docker compose down`（下次啟動 tunnel 網址會變） |

兩個容器都設定 `restart: unless-stopped`：Docker Desktop 開著時，電腦重開機會自動恢復（但 tunnel 網址會變）。

## 注意事項

- **目前是 `Auth__Mode=Dev`**：任何知道網址的人都能用任意名字登入，也能登入別人的名字。只適合給信任的朋友測試；正式開放前必須接上 Firebase 登入（HANDOFF 第 7 節第 4 項）並改成 `Auth__Mode=Firebase`。
- Quick Tunnel 是 Cloudflare 的免費試用服務，沒有可用性保證。要固定網址需要 Cloudflare 帳號 + 自己的網域（Named Tunnel），之後再升級。
- 本機 `127.0.0.1:5080` 仍可直接連（`ws://127.0.0.1:5080/ws`），方便自己用 Unity 測。
