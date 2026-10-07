# 雲端帳號註冊與設定

全部使用免費方案。**密碼、資料庫連線字串、service key 等秘密，不要貼到聊天室、不要寫進程式碼**，之後一律放在伺服器的環境變數。

## 1. GitHub（程式碼）

1. 到 https://github.com 註冊。
2. 右上角「+」→ New repository：名稱 `abyss-kingdom`、選 **Private**、**不要**勾 README → Create。
3. 把專案上傳：用 GitHub Desktop → File → Add local repository → 選解壓縮後的 `abyss-kingdom` 資料夾 → Publish repository（保持勾選 Keep this code private）。若你已在網頁建好同名 repo，改用 Repository → Repository settings → Remote 填入 repo 網址後按 Push。

## 2. Supabase（存檔資料庫）

1. 到 https://supabase.com 用 GitHub 帳號登入 → New project。
2. 名稱 `abyss-kingdom`、Region 選 **Northeast Asia (Tokyo)**、方案 Free；設一組資料庫密碼並自己保存。
3. 建立資料表：左側 **SQL Editor** → New query → 貼上專案裡 `db/001_players.sql` 全部內容 → Run。
4. 之後伺服器需要的兩個值（部署時再用，先不用動）：
   - Project Settings → API → **Project URL**（`https://xxxx.supabase.co`，不是秘密）
   - Project Settings → API → **service_role** key（**秘密**，只放伺服器）

> 免費方案一週沒有使用會暫停；Demo 前到後台按 Restore 即可。

## 3. Firebase（登入、推播、當機回報）

1. 到 https://console.firebase.google.com → 新增專案 `abyss-kingdom`（Google Analytics 可先開啟）。
2. 左側 Build → **Authentication** → 開始使用 → Sign-in method：啟用 **匿名** 與 **Google**。
3. 齒輪 → 專案設定 → 一般：記下 **專案 ID**（不是秘密），伺服器設定 `Firebase__ProjectId` 用。
4. Android App 與 Unity SDK 等客戶端接登入時再設定。

## 4. Oracle Cloud（免費主機）

1. 到 https://www.oracle.com/cloud/free/ 註冊（需要信用卡驗證身分，Always Free 資源不會扣款）。
2. **Home Region 選 Japan East (Tokyo) 或 Japan Central (Osaka)**，註冊後無法更改，免費資源只能開在 Home Region。
3. 先註冊完成即可。開主機、裝 Docker、部署伺服器的步驟會在下一個里程碑提供。

注意：
- 免費 Ampere A1 主機在熱門區域偶爾會顯示容量不足，換時間或換可用區重試。
- 主機 7 天內 CPU、網路、記憶體使用率都低於 20% 可能被回收；帳號閒置 30 天以上可能被停用。

## 完成後請回報

- [ ] GitHub repo 網址
- [ ] Supabase：資料表已建立（不用給我任何金鑰）
- [ ] Firebase 專案 ID
- [ ] Oracle Cloud 註冊完成、Home Region 是哪一個
