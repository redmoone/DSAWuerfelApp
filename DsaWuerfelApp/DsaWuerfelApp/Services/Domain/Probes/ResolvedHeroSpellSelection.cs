namespace DsaWuerfelApp.Services;

internal sealed record ResolvedHeroSpellSelection(
    string? SelectedOptionName,
    ResolvedSpellOption[] SelectedSpellOptions,
    string? SpecializationName,
    int SpecializationModifier,
    string DisplayName)
{
    public int AutomaticSpellModifier => SelectedSpellOptions.Sum(option => option.Modifier);

    public int SpellPreRollZfp => SelectedSpellOptions.Sum(option => option.PreRollZfp);

    public bool SpellRequiresManualInput => SelectedSpellOptions.Any(option => option.RequiresManualCalculation);
}
