namespace AbyssKingdom.Server.Persistence;
public sealed class AllianceAdminAudit
{
    public string Id { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public long AtMs { get; set; }
}
