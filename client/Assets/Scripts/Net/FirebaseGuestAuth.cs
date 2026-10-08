using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AbyssKingdom.Client.Net
{
    /// <summary>
    /// PC 版訪客登入：直接呼叫 Firebase Auth REST API，不使用 Firebase Unity SDK（SDK 的桌面版只供開發用）。
    /// 第一次：accounts:signUp 建立匿名帳號；之後：用存在本機的 refresh token 換新的 ID Token（同一個 UID）。
    /// 取得的 ID Token 送給伺服器，由伺服器的 FirebaseAuthVerifier 驗證。
    /// 注意：refresh token 只存在這台電腦（PlayerPrefs），清掉就等於換新帳號——之後要做「綁定 Google」。
    /// </summary>
    public static class FirebaseGuestAuth
    {
        /// <summary>
        /// Firebase 專案的「網路 API 金鑰」。這是公開識別碼，本來就會打包進客戶端，不是秘密
        /// （安全性靠伺服器驗證 ID Token；可到 Google Cloud 主控台限制此金鑰只能用 Identity Toolkit API）。
        /// </summary>
        public const string ApiKey = "AIzaSyD6Mhm9LCugDASBmeypa2dBtSlw5AtHc9A";

        private const string PrefRefreshToken = "auth.firebaseRefreshToken";
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public static bool IsConfigured => !string.IsNullOrEmpty(ApiKey);

        /// <summary>取得可送給伺服器的 ID Token；沒有帳號就建立一個匿名帳號。</summary>
        public static async Task<string> GetIdTokenAsync()
        {
            if (!IsConfigured) throw new InvalidOperationException("尚未設定 FirebaseGuestAuth.ApiKey");

            var refreshToken = PlayerPrefs.GetString(PrefRefreshToken, "");
            if (!string.IsNullOrEmpty(refreshToken))
            {
                var res = await PostForm("https://securetoken.googleapis.com/v1/token?key=" + ApiKey,
                    new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken });
                if (res != null)
                {
                    Save((string)res["refresh_token"]);
                    return (string)res["id_token"];
                }
                // refresh token 失效（帳號被刪或停用）→ 下面重新建立匿名帳號
                Debug.LogWarning("[FirebaseGuestAuth] refresh token 無效，建立新的訪客帳號");
            }

            var signUp = await PostJson("https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + ApiKey,
                new { returnSecureToken = true });
            if (signUp == null) throw new Exception("建立訪客帳號失敗（請確認 Firebase 已啟用匿名登入、API 金鑰正確）");
            Save((string)signUp["refreshToken"]);
            return (string)signUp["idToken"];
        }

        private static void Save(string refreshToken)
        {
            PlayerPrefs.SetString(PrefRefreshToken, refreshToken);
            PlayerPrefs.Save();
        }

        private static Task<JObject> PostJson(string url, object body) =>
            Send(url, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));

        private static Task<JObject> PostForm(string url, Dictionary<string, string> form) =>
            Send(url, new FormUrlEncodedContent(form));

        /// <summary>成功回傳 JSON；HTTP 4xx（例如 token 失效）回傳 null；網路錯誤直接丟例外。</summary>
        private static async Task<JObject> Send(string url, HttpContent content)
        {
            using (var res = await Http.PostAsync(url, content))
            {
                var text = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode) return JObject.Parse(text);
                Debug.LogWarning($"[FirebaseGuestAuth] HTTP {(int)res.StatusCode}: {text}");
                return null;
            }
        }
    }
}
