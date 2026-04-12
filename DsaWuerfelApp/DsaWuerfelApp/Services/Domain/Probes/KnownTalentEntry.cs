using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

internal sealed class KnownTalentEntry(TalentData talent, bool isOwnedByHero)
{
    public TalentData Talent { get; } = talent;
    public bool IsOwnedByHero { get; } = isOwnedByHero;
}
