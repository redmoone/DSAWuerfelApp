namespace DsaWuerfelApp.Services;

internal sealed record ResolvedHeroSpellSelection(
    string? SelectedOptionName,
    ResolvedSpellOption[] SelectedSpellOptions,
    string? SpecializationName,
    int SpecializationModifier,
    string DisplayName);
