namespace AbyssKingdom.Server.Game;

/// <summary>Single-server operation sequencing; metadata locking never extends over a player mutation.</summary>
public sealed class AllianceSynchronization
{
    internal SemaphoreSlim Operations { get; } = new(1, 1);
    internal SemaphoreSlim Metadata { get; } = new(1, 1);
    private long _revision;
    internal long Revision => Interlocked.Read(ref _revision);
    internal AbyssKingdom.Server.Persistence.IAllianceStore BindStore(AbyssKingdom.Server.Persistence.IAllianceStore backend) =>
        new AbyssKingdom.Server.Persistence.AllianceMetadataStore(backend, Metadata, () => Interlocked.Increment(ref _revision));
}
