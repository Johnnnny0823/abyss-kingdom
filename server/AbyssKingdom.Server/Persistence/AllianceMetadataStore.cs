namespace AbyssKingdom.Server.Persistence;

/// <summary>Short snapshot I/O lock; backend methods must never enter a player/session operation.</summary>
internal sealed class AllianceMetadataStore(IAllianceStore backend, SemaphoreSlim gate, Action written) : IAllianceStore
{
    public async Task<AllianceDirectory> LoadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await backend.LoadAsync(ct); }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(AllianceDirectory d, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await backend.SaveAsync(d, ct); }
        finally { written(); gate.Release(); }
    }
}
