using System.Net;
using System.Net.Http.Json;

using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public sealed class TestApplicationSmokeTests : IClassFixture<TestApplicationFactory>
{
    private readonly TestApplicationFactory _factory;

    public TestApplicationSmokeTests(TestApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_dice_request_is_rejected()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/api/dice/catalog-context");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_catalog_request_works_without_development_database()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        using var response = await client.GetAsync("/api/dice/catalog-context");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var context = await response.Content.ReadFromJsonAsync<DicePageContextDto>();
        Assert.NotNull(context);
        Assert.Contains(context!.AvailableProbes, entry => entry.DisplayLabel.StartsWith("Abvenenum", StringComparison.Ordinal));
        Assert.Contains(context.AvailableProbes, entry => entry.DisplayLabel.StartsWith("Akrobatik", StringComparison.Ordinal));
        Assert.True(File.Exists(_factory.Database.DatabasePath));
        Assert.StartsWith(Path.GetTempPath(), _factory.Database.DatabasePath, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_factory.EmailSender.SentMessages);
    }

    [Fact]
    public async Task Catalog_spell_info_uses_new_rule_sections_without_hero()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var probeValue = Uri.EscapeDataString(
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, "Attributo"));
        using var response = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        Assert.Contains(info!.Sections, section => section.Label == "Probe");
        Assert.Contains(info.Sections, section => section.Label == "Sonderregeln");
        Assert.Contains("Katalogeintrag", info.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_talent_info_contains_prepared_description_and_rules()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var probeValue = Uri.EscapeDataString(
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Talent, "Akrobatik"));
        using var response = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        Assert.Contains(info!.Sections, section => section.Label == "Beschreibung");
        Assert.Contains(info.Sections, section => section.Label == "Effektive Behinderung");
        Assert.Contains(info.Sections, section => section.Label == "Voraussetzung");
        Assert.Contains(info.Sections, section => section.Label == "Regelhinweise");
        Assert.Contains(info.Sections, section => section.Label == "Spezialisierungen");
    }

    [Fact]
    public async Task Catalog_talent_with_multiple_probes_requires_and_exposes_an_explicit_probe()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        using var contextResponse = await client.GetAsync("/api/dice/catalog-context");
        contextResponse.EnsureSuccessStatusCode();
        var context = await contextResponse.Content.ReadFromJsonAsync<DicePageContextDto>();
        var singing = Assert.Single(
            context!.AvailableProbes,
            entry => entry.DisplayLabel.StartsWith("Singen", StringComparison.Ordinal));

        Assert.False(singing.IsSelectable);
        Assert.Equal(2, singing.Alternatives.Length);

        var selectedAlternative = singing.Alternatives[1].Value;
        var probeValue = Uri.EscapeDataString(selectedAlternative);
        using var infoResponse = await client.GetAsync($"/api/dice/probe-info?probeValue={probeValue}");

        infoResponse.EnsureSuccessStatusCode();
        var info = await infoResponse.Content.ReadFromJsonAsync<ProbeInfoResultDto>();
        Assert.NotNull(info);
        var probeSection = Assert.Single(info!.Sections, section => section.Label == "Probe");
        Assert.Contains("IN/CH/CH", probeSection.Text, StringComparison.Ordinal);
        Assert.Contains("IN/CH/KO", probeSection.Text, StringComparison.Ordinal);
        Assert.Contains("alternative Talentprobe", info.DetailsText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Talent_specialization_uses_hero_data_threshold_and_keeps_raw_taw()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "test-user",
            Name = "Talenttest",
            Eigenschaften = new() { ["MU"] = 10, ["GE"] = 10, ["KK"] = 10 },
            Talente = new()
            {
                ["Akrobatik"] = new TalentData
                {
                    Wert = 7,
                    Probe = "MU/GE/KK",
                    Specializations = ["Balancieren", "Bodenakrobatik"]
                }
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            db.Heroes.Add(hero);
            await db.SaveChangesAsync();
        }

        using var contextResponse = await client.GetAsync($"/api/dice/context?heroId={hero.Id}");
        contextResponse.EnsureSuccessStatusCode();
        var context = await contextResponse.Content.ReadFromJsonAsync<DicePageContextDto>();
        var akrobatik = Assert.Single(
            context!.AvailableProbes,
            entry => entry.DisplayLabel.StartsWith("Akrobatik", StringComparison.Ordinal));
        Assert.Contains(akrobatik.Alternatives, alternative =>
            alternative.Label.Contains("Balancieren", StringComparison.Ordinal));
        Assert.DoesNotContain(akrobatik.Alternatives, alternative =>
            alternative.Label.Contains("Bodenakrobatik", StringComparison.Ordinal));

        var selectedSpecialization = ProbeSelectionValue.EncodeOption(
            ProbeSelectionKind.Talent,
            "Akrobatik",
            ProbeSelectionOptionKind.Specialization,
            "Balancieren",
            99);
        var request = new TalentRollRequestDto(
            null,
            hero.Id,
            selectedSpecialization,
            0,
            null,
            [],
            "10,10,10",
            false);

        using var response = await client.PostAsJsonAsync("/api/dice/talent-roll", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TalentRollResultDto>();
        Assert.NotNull(result);
        Assert.Equal(7, result!.TalentValue);
        Assert.Equal(-2, result.SpecializationModifier);
        Assert.Equal(9, result.EffectiveTalentValue);
        Assert.Equal("Balancieren", result.SpecializationName);
    }

    [Fact]
    public async Task Spell_option_modifier_is_resolved_from_catalog_and_keeps_original_zfw()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "test-user");

        var hero = new Hero
        {
            Id = Guid.NewGuid(),
            OwnerUserId = "test-user",
            Name = "Zaubertest",
            Eigenschaften = new() { ["KL"] = 10, ["FF"] = 10 },
            Zauber = new()
            {
                ["Abvenenum"] = new TalentData { Wert = 10, Probe = "KL/KL/FF" }
            }
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            db.Heroes.Add(hero);
            await db.SaveChangesAsync();
        }

        var option = ProbeSelectionValue.EncodeOption(
            ProbeSelectionKind.Spell,
            "Abvenenum",
            ProbeSelectionOptionKind.SpellModification,
            "Zauberdauer: Verlängern");
        var request = new TalentRollRequestDto(
            null,
            hero.Id,
            ProbeSelectionValue.EncodeBase(ProbeSelectionKind.Spell, "Abvenenum"),
            0,
            null,
            [option],
            "10,10,10",
            false);

        using var response = await client.PostAsJsonAsync("/api/dice/talent-roll", request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TalentRollResultDto>();
        Assert.NotNull(result);
        Assert.Equal(10, result!.SpellDetails!.OriginalZfw);
        Assert.Equal(-3, result.SpellDetails.AutomaticModifier);
        Assert.Equal(13, result.EffectiveTalentValue);
        Assert.Equal(13, result.SpellDetails.RawZfp);
        Assert.Equal(13, result.SpellDetails.AvailableZfp);
    }

    [Fact]
    public async Task Existing_history_schema_gets_context_column_idempotently()
    {
        using var database = new TestDatabase();
        using (var initialFactory = new TestApplicationFactory(database))
        using (var initialClient = initialFactory.CreateClient())
        {
            using var response = await initialClient.GetAsync("/api/dice/catalog-context");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var connection = new SqliteConnection(database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO SessionRollHistory
                    (SessionId, PlayerName, TimestampUtc, RollsJson, Modifier, TotalSum)
                VALUES ('legacy-session', 'Altspieler', '2026-01-01T12:00:00Z',
                    '[{"sides":6,"value":4}]', 3, 7);
                ALTER TABLE SessionRollHistory DROP COLUMN ContextJson;
                """;
            command.ExecuteNonQuery();
        }

        using (var factory = new TestApplicationFactory(database))
        using (var client = factory.CreateClient())
        {
            using var response = await client.GetAsync("/api/dice/catalog-context");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using (var factory = new TestApplicationFactory(database))
        using (var client = factory.CreateClient())
        {
            using var response = await client.GetAsync("/api/dice/catalog-context");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        using var verificationConnection = new SqliteConnection(database.ConnectionString);
        verificationConnection.Open();
        using var verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText = "PRAGMA table_info('SessionRollHistory');";
        using var reader = verificationCommand.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("ContextJson", columns);
        Assert.Equal(1, columns.Count(column => column == "ContextJson"));
    }

    [Fact]
    public void Structured_history_context_survives_store_round_trip_and_old_rows_keep_raw_fields()
    {
        var sessionId = $"history-{Guid.NewGuid():N}";
        var timestamp = DateTime.UtcNow;
        var context = new RollHistoryContextDto(
            RollHistoryKind.Talent,
            "Klettern",
            RollHistoryOutcome.Success,
            4,
            [new RollHistoryCheckDto("MU", 12, 10, 2, RollHistoryCheckState.Compensated)]);
        var entry = new RollHistoryEntryDto(
            "Tester",
            timestamp,
            [new DiceRollDto(20, 12), new DiceRollDto(20, 10)],
            -2,
            20,
            context);

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SessionRecordStore>();
            store.AppendHistoryEntry(sessionId, entry);
        }

        RollHistoryEntryDto structuredEntry;
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SessionRecordStore>();
            structuredEntry = Assert.Single(store.LoadHistory(sessionId));
        }

        var loadedContext = Assert.IsType<RollHistoryContextDto>(structuredEntry.Context);
        Assert.Equal(context.Kind, loadedContext.Kind);
        Assert.Equal(context.DisplayName, loadedContext.DisplayName);
        Assert.Equal(context.Outcome, loadedContext.Outcome);
        Assert.Equal(context.RemainingPoints, loadedContext.RemainingPoints);
        Assert.Equal(context.Checks, loadedContext.Checks);

        var oldSessionId = $"old-history-{Guid.NewGuid():N}";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
            db.SessionRollHistoryRecords.Add(new SessionRollHistoryRecord
            {
                SessionId = oldSessionId,
                PlayerName = "Altspieler",
                TimestampUtc = timestamp.AddMinutes(-1),
                RollsJson = "[{\"sides\":6,\"value\":4}]",
                Modifier = 3,
                TotalSum = 7,
                ContextJson = null
            });
            db.SaveChanges();
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SessionRecordStore>();
            var oldEntry = Assert.Single(store.LoadHistory(oldSessionId));
            Assert.Null(oldEntry.Context);
            Assert.Equal("Altspieler", oldEntry.PlayerName);
            Assert.Equal(new DiceRollDto(6, 4), Assert.Single(oldEntry.Rolls));
            Assert.Equal(3, oldEntry.Modifier);
            Assert.Equal(7, oldEntry.TotalSum);
        }
    }
}