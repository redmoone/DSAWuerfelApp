using DsaWuerfelApp.Services;
using DsaWuerfelApp.Shared;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DsaWuerfelApp.Tests;

public class SessionMembershipTests
{
    [Theory]
    [InlineData("master")]
    [InlineData("player")]
    public void Leaving_preserves_remaining_heroes_and_master_role(string leaving)
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        var service = factory.Services.GetRequiredService<SessionService>();
        var session = service.CreateSession("master", "M", null);
        foreach (var id in new[] { "player", "third" }) service.AddPlayer(session.SessionId, new PlayerInfo { UserId = id, Name = id });
        var heroes = new[] { "master", "player", "third" }.ToDictionary(id => id, _ => Guid.NewGuid());
        foreach (var (id, hero) in heroes) service.UpdatePlayerHero(session.SessionId, id, hero, id + " hero");
        Assert.Throws<InvalidOperationException>(() => service.LeaveSession(session.SessionId, "outsider"));
        service.LeaveSession(session.SessionId, leaving);
        var remaining = service.GetSessionDetails(session.SessionId, "third");
        Assert.Equal(2, remaining.Players.Length);
        Assert.Single(remaining.Players, player => player.IsMaster);
        foreach (var player in remaining.Players) { Assert.Equal(heroes[player.UserId], player.ActiveHeroId); Assert.Equal(player.UserId + " hero", player.ActiveHeroName); }
        service.LeaveSession(session.SessionId, remaining.Players.First(player => player.UserId != "third").UserId);
        Assert.True(service.LeaveSession(session.SessionId, "third").SessionDeleted);
        Assert.Empty(service.GetSessionsForUser("third"));
    }
}
