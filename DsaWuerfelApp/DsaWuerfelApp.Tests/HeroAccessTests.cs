using System.Net;
using System.Net.Http.Json;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public class HeroAccessTests
{
    [Theory]
    [InlineData("context")]
    [InlineData("probe-info")]
    [InlineData("talent-roll")]
    [InlineData("attribute-roll")]
    [InlineData("bad-trait-roll")]
    public async Task Known_foreign_hero_is_rejected_over_http(string route)
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var hero = await Seed(factory);
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "outsider");
        var response = route switch
        {
            "context" => await client.GetAsync($"/api/dice/context?heroId={hero.Id}"),
            "probe-info" => await client.GetAsync($"/api/dice/probe-info?heroId={hero.Id}&probeValue=MU"),
            "talent-roll" => await client.PostAsJsonAsync("/api/dice/" + route, new TalentRollRequestDto(null, hero.Id, "unknown", 0, null, [], null, false)),
            "attribute-roll" => await client.PostAsJsonAsync("/api/dice/" + route, new AttributeRollRequestDto(null, hero.Id, ["MU"], 0, null, false)),
            _ => await client.PostAsJsonAsync("/api/dice/" + route, new BadTraitRollRequestDto(null, hero.Id, "Angst", 1, null, false))
        };
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Master_uses_session_targets_and_server_names()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var hero = await Seed(factory);
        var sessions = factory.Services.GetRequiredService<SessionService>();
        var session = sessions.CreateSession("master", "Meister", null);
        sessions.AddPlayer(session.SessionId, new PlayerInfo { UserId = "owner", Name = "Spieler" });
        sessions.UpdatePlayerHero(session.SessionId, "owner", hero.Id, hero.Name);
        var request = new MasterAttributeRollRequestDto(session.SessionId, [new("owner", "forged", hero.Id, "forged")], ["MU"], 0, null);
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "owner");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/dice/master-attribute-roll", request)).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User-Id");
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "master");
        var response = await client.PostAsJsonAsync("/api/dice/master-attribute-roll", request);
        response.EnsureSuccessStatusCode();
        var target = Assert.Single((await response.Content.ReadFromJsonAsync<MasterAttributeRollTargetResultDto[]>())!);
        Assert.Equal("Spieler", target.PlayerName);
        Assert.Equal(hero.Name, target.HeroName);
        Assert.NotNull(target.Result);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/dice/context?heroId={hero.Id}&sessionId={session.SessionId}")).StatusCode);
        var mixed = request with { Targets = [request.Targets[0], new("outsider", "", Guid.NewGuid(), "")] };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/dice/master-attribute-roll", mixed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/dice/master-talent-roll", new MasterTalentRollRequestDto(session.SessionId, mixed.Targets, "x", 0, null, [], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/dice/master-attribute-roll", request with { Targets = [request.Targets[0], request.Targets[0]] })).StatusCode);
    }

    [Fact]
    public async Task Master_can_roll_for_an_offline_player_with_a_persisted_active_hero()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var hero = await Seed(factory, "offline-player", active: true);
        var sessions = factory.Services.GetRequiredService<SessionService>();
        var session = sessions.CreateSession("master", "Meister", null);
        sessions.AddPlayer(session.SessionId, new PlayerInfo { UserId = "offline-player", Name = "Offline-Spieler" });
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "master");

        var details = await client.GetFromJsonAsync<SessionDetailsDto>($"/api/sessions/{session.SessionId}");
        var player = Assert.Single(details!.Players, current => current.UserId == "offline-player");
        Assert.False(player.IsOnline);
        Assert.Equal(hero.Id, player.ActiveHeroId);
        Assert.Equal(hero.Name, player.ActiveHeroName);

        var request = new MasterAttributeRollRequestDto(
            session.SessionId,
            [new("offline-player", "forged", hero.Id, "forged")],
            ["MU"],
            0,
            null);
        var response = await client.PostAsJsonAsync("/api/dice/master-attribute-roll", request);
        response.EnsureSuccessStatusCode();

        var result = Assert.Single((await response.Content.ReadFromJsonAsync<MasterAttributeRollTargetResultDto[]>())!);
        Assert.Equal("Offline-Spieler", result.PlayerName);
        Assert.Equal(hero.Name, result.HeroName);
        Assert.NotNull(result.Result);
    }

    [Fact]
    public async Task Hub_validates_owner_and_ignores_supplied_hero_name()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var hero = await Seed(factory);
        await using var hub = new HubConnectionBuilder().WithUrl("http://localhost/gamehub", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            options.Headers.Add("X-Test-User-Id", "owner");
        }).Build();
        await hub.StartAsync();
        var session = await hub.InvokeAsync<SessionConnectionDto>("CreateSession", "Spieler", "Test");
        await hub.InvokeAsync("UpdateActiveHero", session.SessionId, hero.Id, "forged");
        var sessions = factory.Services.GetRequiredService<SessionService>();
        Assert.Equal(hero.Name, Assert.Single(sessions.GetSessionDetails(session.SessionId, "owner").Players).ActiveHeroName);
        var foreign = await Seed(factory, "foreign");
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("UpdateActiveHero", session.SessionId, foreign.Id, "forged"));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollTalent", new TalentRollRequestDto(session.SessionId, foreign.Id, "x", 0, null, [], null, false)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollAttribute", new AttributeRollRequestDto(session.SessionId, foreign.Id, ["MU"], 0, null, false)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollBadTrait", new BadTraitRollRequestDto(session.SessionId, foreign.Id, "x", 1, null, false)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollFree", new FreeRollRequestDto(session.SessionId, [new(6, 1)], 0, true)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollTalent", new TalentRollRequestDto(session.SessionId, hero.Id, "x", 0, null, [], null, true)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollAttribute", new AttributeRollRequestDto(session.SessionId, hero.Id, ["MU"], 0, null, true)));
        await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("RollBadTrait", new BadTraitRollRequestDto(session.SessionId, hero.Id, "x", 1, null, true)));
        Assert.Empty(sessions.GetSessionDetails(session.SessionId, "owner").History);
        Assert.Equal(hero.Id, Assert.Single(sessions.GetSessionDetails(session.SessionId, "owner").Players).ActiveHeroId);
        await hub.InvokeAsync("UpdateActiveHero", session.SessionId, (Guid?)null, (string?)null);
        Assert.Null(Assert.Single(sessions.GetSessionDetails(session.SessionId, "owner").Players).ActiveHeroId);
    }

    [Theory]
    [InlineData("owner", false, 200)]
    [InlineData("master", false, 404)]
    [InlineData("master", true, 200)]
    [InlineData("player", true, 404)]
    [InlineData("outsider", true, 404)]
    public async Task Context_access_requires_ownership_or_session_master(string caller, bool withSession, int status)
    {
        using var factory = new TestApplicationFactory(); using var client = factory.CreateClient();
        var hero = await Seed(factory);
        var sessions = factory.Services.GetRequiredService<SessionService>();
        var session = sessions.CreateSession("master", "Master", null);
        sessions.AddPlayer(session.SessionId, new PlayerInfo {UserId="owner", Name="Owner"});
        sessions.AddPlayer(session.SessionId, new PlayerInfo {UserId="player", Name="Player"});
        sessions.UpdatePlayerHero(session.SessionId, "owner", hero.Id, hero.Name);
        client.DefaultRequestHeaders.Add("X-Test-User-Id", caller);
        var response = await client.GetAsync($"/api/dice/context?heroId={hero.Id}" + (withSession ? $"&sessionId={session.SessionId}" : ""));
        Assert.Equal(status, (int)response.StatusCode);
    }

    private static async Task<Hero> Seed(TestApplicationFactory factory, string owner = "owner", bool active = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeroDbContext>();
        var hero = new Hero { Id = Guid.NewGuid(), OwnerUserId = owner, IsActive = active, Name = "Testheld", Eigenschaften = new() { ["MU"] = 12 } };
        db.Heroes.Add(hero);
        await db.SaveChangesAsync();
        return hero;
    }
}
