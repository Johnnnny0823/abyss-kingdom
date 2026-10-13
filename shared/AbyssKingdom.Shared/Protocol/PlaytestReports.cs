using System.Linq;
using System.Text.RegularExpressions;

namespace AbyssKingdom.Shared.Protocol
{
    public static class PlaytestReports
    {
        public static string Sanitize(string value, int max)
        {
            string text = value ?? "";
            if (text.Length > max) text = text.Substring(0, max);
            text = Regex.Replace(text, @"(?i)\b(?:bearer\s+\S+|(?:token|apikey|api_key|password|servicekey)\s*[:=]\s*\S+)", "[secret]");
            text = Regex.Replace(text, @"(?i)(?:[A-Z]:[\\/]|\\\\)[^\r\n\t""<>]*|(?:/Users/|/home/|/tmp/|/var/)[^\s""<>]*", "[path]");
            text = Regex.Replace(text, @"\b(?:\d{1,3}\.){3}\d{1,3}\b|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|https?://[^\s]+", "[private]");
            text = Regex.Replace(text, @"(?i)(?<![\w])(?:[0-9a-f]{1,4}:){2,}[0-9a-f:]+", "[private]");
            text = Regex.Replace(text, @"(?<![\w:])/(?!/)[^\s""<>]+", "[path]");
            text = new string(text.Where(c => !char.IsControl(c) || c == '\n' || c == '\t').ToArray()).Trim();
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}
