using System.Globalization;
using System.Linq;
using System.Text;

namespace AbyssKingdom.Shared.Social
{

public enum AllianceRole { Member, Officer, Leader }
public enum AllianceJoinMode { Open, Approval }
public enum AlliancePermission { ReviewApplications, KickMember, ManageOfficers, EditAnnouncement, ChangeJoinMode, ManageFlags, TransferLeadership, Dissolve }

public static class AllianceConfig
{
    public const int GuideJoined = 1, GuideHelped = 2, GuideRallied = 4;
    public const int JoinKeepLevel = 2;
    public const int CreateKeepLevel = 3;
    public const long CreateWood = 5_000;
    public const long CreateStone = 5_000;
    public const int MaxMembers = 30;
    public const int MaxOfficers = 4;
    public const int MaxApplications = 3;
    public const long ApplicationDurationMs = 24L * 60 * 60 * 1000;
    public const long RejoinCooldownMs = 24L * 60 * 60 * 1000;
}

public static class AllianceRules
{
    public static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string name = value.Trim().Normalize(NormalizationForm.FormKC);
        int length = new StringInfo(name).LengthInTextElements;
        if (length is < 2 or > 16 || name.Any(c => char.IsControl(c) || c is '<' or '>')) return null;
        return name;
    }

    public static string NormalizeTag(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string tag = value.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        return tag.Length is >= 2 and <= 4 && tag.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9') ? tag : null;
    }

    public static bool Can(AllianceRole role, AlliancePermission permission) => permission switch
    {
        AlliancePermission.ReviewApplications or AlliancePermission.KickMember or AlliancePermission.EditAnnouncement or AlliancePermission.ManageFlags
            => role is AllianceRole.Leader or AllianceRole.Officer,
        AlliancePermission.ChangeJoinMode or AlliancePermission.ManageOfficers or AlliancePermission.TransferLeadership or AlliancePermission.Dissolve
            => role == AllianceRole.Leader,
        _ => false
    };

    public static bool CanKick(AllianceRole actor, AllianceRole target, bool samePlayer)
        => !samePlayer && target != AllianceRole.Leader &&
           (actor == AllianceRole.Leader || actor == AllianceRole.Officer && target == AllianceRole.Member);

    public static bool CooldownEnded(long nowMs, long untilMs) => nowMs >= untilMs;
    public static bool ApplicationActive(long nowMs, long expiresAtMs) => nowMs < expiresAtMs;
}
}
