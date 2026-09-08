using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed record ResolvedProbeData(
    ProbeSelectionKind Kind,
    string BaseName,
    string Name,
    TalentData ProbeData,
    string? SelectedOptionName,
    ProbeSelectionOptionKind SelectedOptionKind,
    ResolvedSpellOption[] SelectedSpellOptions,
    string? SpecializationName,
    int SpecializationModifier)
{
    public bool UsesCatalogValue { get; init; }

    public int AutomaticSpellModifier => SelectedSpellOptions.Sum(option => option.Modifier);

    public int SpellPreRollZfp => SelectedSpellOptions.Sum(option => option.PreRollZfp);

    public bool SpellRequiresManualInput => SelectedSpellOptions.Any(option => option.RequiresManualCalculation);
}

public sealed record ResolvedSpellOption(
    string Name,
    string DisplayName,
    ProbeSelectionOptionKind Kind,
    int Modifier,
    int PreRollZfp,
    bool RequiresManualCalculation);
