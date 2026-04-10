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
    int SpecializationModifier);

public sealed record ResolvedSpellOption(string Name, ProbeSelectionOptionKind Kind, int Modifier);