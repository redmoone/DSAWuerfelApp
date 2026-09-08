using System.Net;
using System.Net.Http.Json;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace DsaWuerfelApp.Tests;

public class HeroActivationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Activation_is_owned_idempotent_and_rolls_back_on_failure()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", "owner");
        await using var db = new HeroDbContext(new DbContextOptionsBuilder<HeroDbContext>().UseSqlite(factory.Database.ConnectionString).Options);
        var a = new Hero { Id = Guid.NewGuid(), OwnerUserId = "owner", IsActive = true };
        var b = new Hero { Id = Guid.NewGuid(), OwnerUserId = "owner" };
        var foreign = new Hero { Id = Guid.NewGuid(), OwnerUserId = "foreign", IsActive = true };
        db.AddRange(a, b, foreign); await db.SaveChangesAsync();
        foreach (var id in new[] { b.Id, b.Id, a.Id })
        {
            var response = await client.PutAsync($"/api/heroes/{id}/activate", null);
            response.EnsureSuccessStatusCode();
            Assert.True((await response.Content.ReadFromJsonAsync<Hero>())!.IsActive);
            Assert.Equal(id, (await db.Heroes.AsNoTracking().SingleAsync(hero => hero.OwnerUserId == "owner" && hero.IsActive)).Id);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsync($"/api/heroes/{foreign.Id}/activate", null)).StatusCode);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_activation BEFORE UPDATE OF IsActive ON Heroes WHEN NEW.IsActive = 1 BEGIN SELECT RAISE(ABORT, 'simulated'); END;");
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PutAsync($"/api/heroes/{b.Id}/activate", null)).StatusCode);
        Assert.Equal(a.Id, (await db.Heroes.AsNoTracking().SingleAsync(hero => hero.OwnerUserId == "owner" && hero.IsActive)).Id);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_activation;");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { a.Id, b.Id }.Select(id => Task.Run(async () => { await start.Task; return await client.PutAsync($"/api/heroes/{id}/activate", null); })).ToArray();
        start.SetResult();
        foreach (var response in await Task.WhenAll(tasks)) response.EnsureSuccessStatusCode();
        Assert.Equal(1, await db.Heroes.CountAsync(hero => hero.OwnerUserId == "owner" && hero.IsActive));
    }

    [Fact]
    public async Task Native_sqlite_is_at_least_the_patched_version()
    {
        using var factory = new TestApplicationFactory();
        using var client = factory.CreateClient();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(factory.Database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)(await command.ExecuteScalarAsync())!);
        output.WriteLine($"Native SQLite: {version}");
        Assert.True(version >= new Version(3, 50, 2), $"Native SQLite: {version}");
    }
}
