using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Tests;

public sealed class TalentProbeEvaluatorTests
{
    [Theory]
    [InlineData(5, 0, new[] { 10, 10, 10 }, new[] { 10, 10, 10 }, TalentProbeStatus.Bestanden, 5, 5)]
    [InlineData(5, 0, new[] { 10, 10, 10 }, new[] { 12, 13, 10 }, TalentProbeStatus.Bestanden, 0, 5)]
    [InlineData(5, 0, new[] { 10, 10, 10 }, new[] { 12, 14, 10 }, TalentProbeStatus.NichtBestanden, -1, 5)]
    [InlineData(0, 2, new[] { 10, 10, 10 }, new[] { 8, 8, 8 }, TalentProbeStatus.Bestanden, 0, -2)]
    [InlineData(0, 2, new[] { 10, 10, 10 }, new[] { 9, 8, 8 }, TalentProbeStatus.NichtBestanden, -1, -2)]
    [InlineData(5, 0, new[] { 10, 10, 10 }, new[] { 1, 1, 20 }, TalentProbeStatus.GluecklicherWurf, 0, 5)]
    [InlineData(5, 0, new[] { 10, 10, 10 }, new[] { 20, 20, 1 }, TalentProbeStatus.Patzer, 0, 5)]
    public void Evaluate_returns_expected_baseline(
        int talentValue,
        int modifier,
        int[] attributes,
        int[] rolls,
        TalentProbeStatus expectedStatus,
        int expectedRest,
        int expectedEffectiveTalentValue)
    {
        var result = TalentProbeEvaluator.Evaluate(talentValue, modifier, attributes, rolls);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(expectedRest, result.Rest);
        Assert.Equal(expectedEffectiveTalentValue, result.EffectiveTalentValue);
    }

    [Fact]
    public void Evaluate_rejects_wrong_attribute_count()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TalentProbeEvaluator.Evaluate(5, 0, [10, 10], [10, 10, 10]));

        Assert.NotNull(exception);
    }

    [Fact]
    public void Evaluate_rejects_wrong_roll_count()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            TalentProbeEvaluator.Evaluate(5, 0, [10, 10, 10], [10, 10]));

        Assert.NotNull(exception);
    }

    [Fact]
    public void Spell_calculation_keeps_zfw_and_applies_pre_roll_zfp_separately()
    {
        var service = new TalentProbeService(new DiceService());
        var result = service.RollTalentProbe(
            new ResolvedTalentRollRequest(
                "Testzauber",
                10,
                ProbeAttributes.Create("KL/KL/FF"),
                [10, 10, 10],
                0,
                null,
                0,
                null,
                0,
                ForcedRollValues.CreateOptional("10,10,10", 3),
                2,
                3,
                new SpellRollCalculationContext(true, ["Testoption"])),
            "Tester");

        Assert.Equal(10, result.TalentValue);
        Assert.Equal(8, result.EffectiveTalentValue);
        Assert.Equal(2, result.SpellDetails!.AutomaticModifier);
        Assert.Equal(3, result.SpellDetails.PreRollZfp);
        Assert.Equal(8, result.SpellDetails.RawZfp);
        Assert.Equal(5, result.SpellDetails.AvailableZfp);
        Assert.True(result.SpellDetails.ManualModifierRequired);
    }
}
