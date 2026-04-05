using DsaWuerfelApp.Shared.Models;

namespace DsaWuerfelApp.Services;

public sealed record ResolvedTalentData(
    string Name,
    TalentData Talent,
    string? SpecializationName,
    int SpecializationModifier);