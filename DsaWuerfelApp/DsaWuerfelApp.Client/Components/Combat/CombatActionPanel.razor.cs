using System.Globalization;

using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components.Combat;

public partial class CombatActionPanel
{
    private static readonly int[] EmptyDice = [];

    [Parameter] public IReadOnlyList<CombatSetVariantDto> Sets { get; set; } = Array.Empty<CombatSetVariantDto>();
    [Parameter] public bool IsLoading { get; set; }
    [Parameter] public string? SelectedSetId { get; set; }
    [Parameter] public EventCallback<string> SetSelected { get; set; }
    [Parameter] public bool LoadoutLocked { get; set; }
    [Parameter] public bool AttributeMode { get; set; }
    [Parameter] public EventCallback<bool> AttributeModeChanged { get; set; }
    [Parameter] public IReadOnlyList<CombatWeaponDto> Weapons { get; set; } = Array.Empty<CombatWeaponDto>();
    [Parameter] public string? SelectedWeaponId { get; set; }
    [Parameter] public EventCallback<string> WeaponSelected { get; set; }
    [Parameter] public bool IsSessionCombat { get; set; }
    [Parameter] public string SelectedAction { get; set; } = CombatActionIds.Attack;
    [Parameter] public EventCallback<string> ActionSelected { get; set; }
    [Parameter] public IReadOnlyList<ActionOption> Actions { get; set; } = Array.Empty<ActionOption>();
    [Parameter] public IReadOnlyList<ProbeSearchEntryDto> Maneuvers { get; set; } = Array.Empty<ProbeSearchEntryDto>();
    [Parameter] public string SelectedProbe { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> ProbeSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatAttributeDto> Attributes { get; set; } = Array.Empty<CombatAttributeDto>();
    [Parameter] public IReadOnlyList<string> SelectedAttributes { get; set; } = Array.Empty<string>();
    [Parameter] public EventCallback<string> AttributeSelected { get; set; }
    [Parameter] public EventCallback<int> AttributeRemoved { get; set; }
    [Parameter] public int Modifier { get; set; }
    [Parameter] public EventCallback<int> ModifierChanged { get; set; }
    [Parameter] public string RollText { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> RollTextChanged { get; set; }
    [Parameter] public EventCallback ResetRequested { get; set; }
    [Parameter] public EventCallback RollRequested { get; set; }
    [Parameter] public bool CanRoll { get; set; }
    [Parameter] public bool IsBusy { get; set; }
    [Parameter] public int? BaseTarget { get; set; }
    [Parameter] public int? EffectiveTarget { get; set; }
    [Parameter] public CombatFacing Facing { get; set; }
    [Parameter] public EventCallback<CombatFacing> FacingChanged { get; set; }
    [Parameter] public IReadOnlyList<int> ResultDiceSides { get; set; } = Array.Empty<int>();
    [Parameter] public IReadOnlyList<int> ResultDiceValues { get; set; } = Array.Empty<int>();
    [Parameter] public long ResultVersion { get; set; }
    [Parameter] public CombatAttackExchangeDto? ActiveExchange { get; set; }
    [Parameter] public string? ActiveExchangeAttackerName { get; set; }
    [Parameter] public string? ActiveExchangeTargetName { get; set; }
    [Parameter] public bool CanRespondToExchange { get; set; }
    [Parameter] public bool CanResolveActiveExchange { get; set; }
    [Parameter] public EventCallback<CombatActionKind> ExchangeActionRequested { get; set; }
    [Parameter] public bool CanHoldAction { get; set; }
    [Parameter] public EventCallback HoldActionRequested { get; set; }
    [Parameter] public bool CanOrient { get; set; }
    [Parameter] public bool OrientationDisabled { get; set; }
    [Parameter] public EventCallback OrientationRequested { get; set; }
    [Parameter] public CombatSessionActionDto? OrientationAction { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> OrientationCheckRequested { get; set; }
    [Parameter] public EventCallback<CombatSessionActionDto> OrientationCompleteRequested { get; set; }
    [Parameter] public bool OpponentMode { get; set; }
    [Parameter] public IReadOnlyList<CombatSessionParticipantDto> OpponentParticipants { get; set; } = Array.Empty<CombatSessionParticipantDto>();
    [Parameter] public string? SelectedOpponentParticipantId { get; set; }
    [Parameter] public EventCallback<string> OpponentParticipantSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatSessionParticipantDto> OpponentTargets { get; set; } = Array.Empty<CombatSessionParticipantDto>();
    [Parameter] public string? SelectedOpponentTargetId { get; set; }
    [Parameter] public EventCallback<string> OpponentTargetSelected { get; set; }
    [Parameter] public IReadOnlyList<CombatEnemyAttackDto> OpponentAttacks { get; set; } = Array.Empty<CombatEnemyAttackDto>();
    [Parameter] public string? SelectedOpponentAttackId { get; set; }
    [Parameter] public EventCallback<string> OpponentAttackSelected { get; set; }

    private IEnumerable<ActionOption> MainActions => Actions.Where(action => action.Key is
        CombatActionIds.Attack or CombatActionIds.Parry or CombatActionIds.ShieldParry or
        CombatActionIds.Dodge or CombatActionIds.Ranged);
    private IEnumerable<ActionOption> AdditionalActions => Actions.Where(action => action.Key is
        CombatActionIds.Damage or CombatActionIds.Zone or CombatActionIds.Initiative);
    private ActionOption? SelectedActionOption => Actions.FirstOrDefault(action => action.Key == SelectedAction);

    private string EmptyStateTitle => IsLoading ? "Kampfprofil wird geladen." : "Noch kein Kampfprofil geladen.";

    private string EmptyStateText => IsLoading
        ? "Das Kampfprofil wird geladen."
        : "Lade ein Kampfprofil, um Kampfwürfe vorzubereiten.";

    private string SelectedSetSummary => Sets.FirstOrDefault(set => set.Id == SelectedSetId) is { } set
        ? GetSetLabel(set)
        : "Kein Kampfset";

    private string SelectedWeaponSummary => Weapons.FirstOrDefault(weapon => weapon.Id == SelectedWeaponId) is { } weapon
        ? GetWeaponLabel(weapon)
        : "Keine Waffe";

    private string EffectiveTargetText => EffectiveTarget?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private string TargetHeadline => EffectiveTarget.HasValue
        ? $"{ActiveRollTitle} auf {EffectiveTargetText}"
        : ActiveRollTitle;

    private bool HasResultDice => ResultDiceSides.Count > 0;

    private CombatEnemyAttackDto? SelectedOpponentAttack =>
        OpponentAttacks.FirstOrDefault(attack => attack.Id == SelectedOpponentAttackId);

    private string OpponentRollButtonText => SelectedOpponentAttack?.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase) == true
        ? "Fernkampf würfeln"
        : "Attacke würfeln";

    private string OpponentRollTitle => SelectedOpponentAttack?.Name ?? "Gegnerangriff";

    private static string GetOpponentAttackValue(CombatEnemyAttackDto attack) =>
        attack.Category.Contains("ranged", StringComparison.OrdinalIgnoreCase)
            ? $"FK {FormatNumber(attack.RangedValue)} · TP {attack.Damage?.Notation ?? "—"}"
            : $"AT {FormatNumber(attack.Attack)} · TP {attack.Damage?.Notation ?? "—"}";

    private string ActiveRollTitle => AttributeMode
        ? "Eigenschaftsprobe"
        : SelectedActionOption?.Label ?? "Kampfwurf";

    private string ActiveExchangeStatusText => ActiveExchange?.Status switch
    {
        CombatExchangeStatus.Declared => "AT-Wurf offen",
        CombatExchangeStatus.AttackOpen => "AT-Wurf offen",
        CombatExchangeStatus.DefenseOpen => "Abwehrentscheidung offen",
        CombatExchangeStatus.Hit => "Treffer bestätigt · Trefferfolge offen",
        CombatExchangeStatus.DamageOpen => "Schaden offen",
        CombatExchangeStatus.Avoided => "Angriff abgewehrt",
        CombatExchangeStatus.Completed => "Austausch abgeschlossen",
        CombatExchangeStatus.Cancelled => "Austausch abgebrochen",
        _ => ""
    };

    private string ActiveExchangePrompt => ActiveExchange?.Status switch
    {
        CombatExchangeStatus.Declared or CombatExchangeStatus.AttackOpen =>
            $"{ActiveExchangeAttackerName ?? "Angreifer"} würfelt die Attacke.",
        CombatExchangeStatus.DefenseOpen =>
            $"{ActiveExchangeTargetName ?? "Ziel"} wählt die Abwehr.",
        CombatExchangeStatus.Hit or CombatExchangeStatus.DamageOpen =>
            string.IsNullOrWhiteSpace(ActiveExchange?.RuleNote)
                ? "Trefferfolge wird abgeschlossen."
                : ActiveExchange.RuleNote,
        _ => string.Empty
    };

    private static string GetExchangeActionLabel(CombatActionKind action) => action switch
    {
        CombatActionKind.WeaponParry => "Waffenparade",
        CombatActionKind.ShieldParry => "Schildparade",
        CombatActionKind.Dodge => "Ausweichen",
        _ => action.ToString()
    };

    private string RollButtonText => AttributeMode
        ? "Eigenschaften würfeln"
        : SelectedAction switch
        {
            CombatActionIds.Attack => "Attacke würfeln",
            CombatActionIds.Parry => "Waffenparade würfeln",
            CombatActionIds.ShieldParry => "Schildparade würfeln",
            CombatActionIds.Dodge => "Ausweichen würfeln",
            CombatActionIds.Ranged => "Fernkampf würfeln",
            CombatActionIds.Damage => "TP würfeln",
            CombatActionIds.Zone => "Trefferzone würfeln",
            CombatActionIds.WoundHelper => "Wund-Hilfswurf",
            CombatActionIds.FumbleHelper => "Patzer-Hilfswurf",
            CombatActionIds.Initiative => "Initiative würfeln",
            _ => "Wurf ausführen"
        };

    private string RollButtonAriaLabel => AttributeMode ? "Eigenschaftsprobe ausführen" : RollButtonText;

    private CombatActionKind? GetActionKind() => CombatActionIds.ToKind(SelectedAction);

    private async Task HandleWeaponChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        if (!string.IsNullOrWhiteSpace(value))
        {
            await WeaponSelected.InvokeAsync(value);
        }
    }

    private Task HandleSetChanged(ChangeEventArgs args)
    {
        var value = args.Value?.ToString();
        return string.IsNullOrWhiteSpace(value) ? Task.CompletedTask : SetSelected.InvokeAsync(value);
    }

    private static string GetSetLabel(CombatSetVariantDto set)
    {
        var model = set.ArmorModel switch
        {
            CombatArmorModel.Zone => "Zonenrüstung",
            CombatArmorModel.Simple => "Einfache Rüstung",
            _ => "Rüstungsmodell unbekannt"
        };
        return $"Set {set.Number} · {model}";
    }

    private static string GetWeaponLabel(CombatWeaponDto weapon)
    {
        var number = weapon.Number is { } value ? $" Nr. {value}" : string.Empty;
        return $"{weapon.Name}{number}";
    }

    private static string GetWeaponValue(CombatWeaponDto weapon) => weapon.Category switch
    {
        CombatWeaponCategory.Ranged => FormatValue("FK", weapon.RangedValue),
        CombatWeaponCategory.Shield => FormatValue("PA", weapon.Parry),
        _ => $"AT {FormatNumber(weapon.Attack)} · PA {FormatNumber(weapon.Parry)}"
    };

    private static string FormatValue(string label, int? value) => $"{label} {FormatNumber(value)}";

    private static string FormatNumber(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static string FormatModifier(int value) => value > 0 ? $"+{value}" : value.ToString(CultureInfo.InvariantCulture);

    public sealed record ActionOption(string Key, string Label, string Detail, bool IsAvailable);

}
