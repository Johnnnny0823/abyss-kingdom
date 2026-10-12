using AbyssKingdom.Server.Admin;
using AbyssKingdom.Server.Auth;
using AbyssKingdom.Server.Game;
using AbyssKingdom.Server.Infrastructure;
using AbyssKingdom.Server.Net;
using AbyssKingdom.Server.Persistence;

namespace AbyssKingdom.Server;

/// <summary>
/// 組裝伺服器。抽成方法是為了讓測試可以替換時鐘、存檔等元件。
/// 目前是「模組化單體」：帳號、城市、（之後的）地圖、戰鬥、社交都在同一個程式裡，按資料夾分模組。
/// </summary>
public static class GameServer
{
    public static WebApplication Build(string[] args, Action<IServiceCollection>? overrideServices = null, string? urls = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        if (urls != null) builder.WebHost.UseUrls(urls);

        var services = builder.Services;
        var config = builder.Configuration;

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton(Json.Options);
        services.AddHttpClient();

        // 登入驗證：Dev（本機開發）或 Firebase（正式）
        // 只接受明確列出的值：拼錯就拒絕啟動，不會默默退回可偽造身分的 Dev 或關機就清空的記憶體存檔
        var authMode = config["Auth:Mode"] ?? "Dev";
        if (authMode.Equals("Firebase", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAuthVerifier, FirebaseAuthVerifier>();
        else if (authMode.Equals("Dev", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IAuthVerifier, DevAuthVerifier>();
        else
            throw new InvalidOperationException($"Unknown Auth:Mode '{authMode}' (expected Firebase or Dev)");

        // 存檔：Memory / InMemory（本機開發，關掉就沒了）或 Supabase（正式）
        var storeMode = config["Storage:Mode"] ?? "Memory";
        if (storeMode.Equals("Supabase", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPlayerStore, SupabasePlayerStore>();
            services.AddSingleton<IAllianceStore, SupabaseAllianceStore>();
        }
        else if (storeMode.Equals("Memory", StringComparison.OrdinalIgnoreCase) || storeMode.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IPlayerStore, InMemoryPlayerStore>();
            services.AddSingleton<IAllianceStore, InMemoryAllianceStore>();
        }
        else
            throw new InvalidOperationException($"Unknown Storage:Mode '{storeMode}' (expected Supabase or Memory)");

        services.AddSingleton<WorldService>();
        services.AddSingleton<AllianceSynchronization>();
        services.AddSingleton<AllianceService>();
        services.AddSingleton<SessionManager>();
        services.AddHostedService(sp => sp.GetRequiredService<SessionManager>());
        services.AddHostedService<AllianceMilitaryTicker>();
        services.AddSingleton<WebSocketHandler>();

        overrideServices?.Invoke(services);

        var app = builder.Build();

        app.Logger.LogInformation("Auth mode: {Auth}, Storage mode: {Storage}", authMode, storeMode);

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

        app.MapGet("/", () => "Abyss Kingdom server is running.");
        app.MapGet("/healthz", (SessionManager sm) => Results.Ok(new { status = "ok", online = sm.OnlineCount }));

        app.Map("/ws", async (HttpContext ctx, WebSocketHandler handler) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("WebSocket only");
                return;
            }
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
            await handler.RunAsync(socket, ctx.RequestAborted);
        });

        AdminEndpoints.Map(app);

        return app;
    }
}
