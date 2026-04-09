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
    string? SpecializationName,
    int SpecializationModifier);