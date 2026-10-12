using System.Security.Cryptography;
using System.Text;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Shared.City;

namespace AbyssKingdom.Server.Admin;

/// <summary>GM 發放內容：各欄位都可省略，數值為「增加量」（可為負數以扣除，結果不會低於 0）。</summary>
public sealed class AdminGrant
{
    public Dictionary<ResourceType, long>? Resources { get; set; }
    public Dictionary<ItemType, long>? Items { get; set; }
    public Dictionary<TroopType, long>? Troops { get; set; }
    /// <summary>直接設定建築等級（測試與美術檢查用，限 0~最高等級）。</summary>
    public Dictionary<BuildingType, int>? Buildings { get; set; }

    public void ApplyTo(CityState city)
    {
        foreach (var (k, v) in Buildings ?? new()) city.Buildings[k] = Math.Clamp(v, 0, CityConfig.MaxLevel);
        foreach (var (k, v) in Resources ?? new()) city.Resources[k] = Math.Max(0, city.GetResource(k) + v);
        foreach (var (k, v) in Items ?? new()) city.Items[k] = Math.Max(0, city.GetItemCount(k) + v);
        foreach (var (k, v) in Troops ?? new()) city.Troops[k] = Math.Max(0, city.GetTroops(k) + v);
    }
}

public sealed class AdminAllianceRetry { public string? Reason { get; set; } }

/// <summary>
/// 開發者用的 GM 工具（測試補資源、道具、部隊）。
/// 只有設定了 Admin:Token（環境變數 Admin__Token）才會開啟；請求必須帶 X-Admin-Token 標頭。
/// 用法見 docs/DEPLOY.md。
/// </summary>
public static class AdminEndpoints
{
    public static void Map(WebApplication app)
    {
        var token = app.Configuration["Admin:Token"];
        if (string.IsNullOrEmpty(token)) return;
        if (token.Length < 24) throw new InvalidOperationException("Admin:Token 太短（至少 24 個字元）");
        app.Logger.LogWarning("Admin endpoints enabled");

        app.MapPost("/admin/grant/{playerId}", async (string playerId, AdminGrant grant, HttpContext ctx, SessionManager sessions) =>
        {
            if (!Authorized(ctx, token)) return Results.Unauthorized();
            var found = await sessions.ModifyCityAsync(playerId, grant.ApplyTo, ctx.RequestAborted);
            app.Logger.LogInformation("Admin grant to {Id}: found={Found}", playerId, found);
            return found ? Results.Ok(new { ok = true }) : Results.NotFound(new { error = "player not found" });
        });
        app.MapGet("/admin/alliance", async (HttpContext ctx, AllianceService alliances) =>
        {
            if (!Authorized(ctx, token)) return Results.Unauthorized();
            return Results.Json(await alliances.SnapshotAsync(ctx.RequestAborted), AbyssKingdom.Server.Infrastructure.Json.Options);
        });
        app.MapPost("/admin/alliance/retry", async (AdminAllianceRetry request, HttpContext ctx, AllianceService alliances, SessionManager sessions) =>
        {
            if (!Authorized(ctx, token)) return Results.Unauthorized();
            string reason = request.Reason?.Trim() ?? "";
            if (reason.Length == 0 || reason.Length > 200 || reason.Any(char.IsControl)) return Results.BadRequest(new { error = "reason required, maximum 200 characters" });
            await alliances.RecordAdminRetryAsync(reason, ctx.RequestAborted);
            await alliances.RecoverFlagPaymentsAsync(ctx.RequestAborted);
            await alliances.AdvanceReinforcementsAsync(ctx.RequestAborted);
            await sessions.RecoverCampSettlementsAsync(ctx.RequestAborted);
            await sessions.RecoverAllianceDefensesAsync(ctx.RequestAborted);
            await alliances.AdvanceRalliesAsync(ctx.RequestAborted);
            return Results.Ok(new { ok = true });
        });
    }

    private static bool Authorized(HttpContext ctx, string token)
    {
        var given = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Admin-Token"].ToString());
        return CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(token));
    }
}
