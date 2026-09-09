using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class RollHistoryContextTests
{
    [Fact]
    public void Talent_success_keeps_a_compensated_check_and_remaining_points()
    {
        var service = new TalentProbeService(new DiceService());

        var result = service.RollTalentProbe(
            new ResolvedTalentRollRequest(
                "Klettern",
                3,
                ProbeAttributes.Create("MU/GE/KK"),
                [10, 10, 10],
                0,
                null,
                0,
                null,
                0,
                ForcedRollValues.CreateOptional("12,10,10", 3)),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Equal(RollHistoryKind.Talent, context.Kind);
        Assert.Equal(RollHistoryOutcome.Success, context.Outcome);
        Assert.Equal(1, context.RemainingPoints);
        Assert.Equal(RollHistoryCheckState.Compensated, context.Checks[0].State);
    }

    [Fact]
    public void Talent_failure_marks_an_uncompensated_check_as_failed()
    {
        var service = new TalentProbeService(new DiceService());

        var result = service.RollTalentProbe(
            new ResolvedTalentRollRequest(
                "Klettern",
                1,
                ProbeAttributes.Create("MU/GE/KK"),
                [10, 10, 10],
                0,
                null,
                0,
                null,
                0,
                ForcedRollValues.CreateOptional("15,10,10", 3)),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Equal(RollHistoryOutcome.Failure, context.Outcome);
        Assert.Contains(context.Checks, check => check.State == RollHistoryCheckState.Failed);
    }

    [Fact]
    public void Spell_probe_uses_spell_kind_and_keeps_zfp_rest()
    {
        var service = new TalentProbeService(new DiceService());

        var result = service.RollTalentProbe(
            new ResolvedTalentRollRequest(
                "Abvenenum",
                5,
                ProbeAttributes.Create("KL/KL/FF"),
                [10, 10, 10],
                0,
                null,
                0,
                null,
                0,
                ForcedRollValues.CreateOptional("10,10,10", 3),
                SpellContext: new SpellRollCalculationContext(false, ["Variante"])),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Equal(RollHistoryKind.Spell, context.Kind);
        Assert.Equal(RollHistoryOutcome.Success, context.Outcome);
        Assert.Equal(5, context.RemainingPoints);
    }

    [Fact]
    public void Attribute_failure_has_structured_checks_without_a_requirement()
    {
        var service = new AttributeProbeService(new DiceService());

        var result = service.RollAttributeProbe(
            new ResolvedAttributeRollRequest(
                AttributeSelection.Create(["MU"]),
                [10],
                0,
                null,
                0,
                "Mutprobe",
                ForcedRollValues.CreateOptional("11", 1)),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Null(result.Requirement);
        Assert.Equal(RollHistoryKind.Attribute, context.Kind);
        Assert.Equal(RollHistoryOutcome.Failure, context.Outcome);
        Assert.Equal(RollHistoryCheckState.Failed, context.Checks[0].State);
        Assert.Equal(1, context.Checks[0].Difference);
    }

    [Fact]
    public void Bad_trait_history_uses_bad_trait_kind_and_success_outcome_when_the_hero_resists()
    {
        var service = new SchlechteEigenschaftProbeService(new DiceService());

        var result = service.RollProbe(
            new ResolvedBadTraitRollRequest(
                "Aberglaube",
                10,
                ForcedRollValues.CreateOptional("15", 1)),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Equal(RollHistoryKind.BadTrait, context.Kind);
        Assert.Equal(RollHistoryOutcome.Success, context.Outcome);
        Assert.Equal("Aberglaube", context.Checks[0].Name);
        Assert.Equal(15, context.Checks[0].Roll);
        Assert.Equal(10, context.Checks[0].TargetValue);
    }

    [Fact]
    public void Free_roll_history_uses_neutral_outcome()
    {
        var handler = new RollFreeHandler(new DiceService());

        var result = handler.Handle(
            new FreeRollRequestDto(null, [new DiceRollGroupDto(6, 1)], 2, false),
            "Tester");

        var context = Assert.IsType<RollHistoryContextDto>(result.HistoryEntry.Context);
        Assert.Equal(RollHistoryKind.Free, context.Kind);
        Assert.Equal(RollHistoryOutcome.None, context.Outcome);
        Assert.Empty(context.Checks);
    }
}