using System.Security.Cryptography;
using System.Text;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Services.Auth;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace DsaWuerfelApp.Tests;

public class MagicLinkConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class SaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw new IOException("simulated save failure");
    }
    private static HeroDbContext Open(TestDatabase database, bool fail = false)
    {
        var builder = new DbContextOptionsBuilder<HeroDbContext>().UseSqlite(database.ConnectionString);
        if (fail) builder.AddInterceptors(new SaveFailure());
        return new HeroDbContext(builder.Options);
    }
    private static MagicLinkToken Token(string raw, int seconds = 60) => new()
    {
        Email = "same@example.test", TokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))),
        RequestedAtUtc = Now.UtcDateTime, ExpiresAtUtc = Now.AddSeconds(seconds).UtcDateTime
    };
    private static async Task<MagicLinkVerificationResult?> Verify(TestDatabase database, string raw, bool fail = false)
    {
        await using var db = Open(database, fail);
        return await new MagicLinkService(db, new FakeMagicLinkEmailSender(), Options.Create(new MagicLinkAuthOptions()), new Clock()).VerifyAsync(raw);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_verification_serializes_consumption_and_user_creation(bool sameToken)
    {
        using var database = new TestDatabase();
        await using (var db = Open(database))
        {
            await db.Database.EnsureCreatedAsync();
            db.MagicLinkTokens.Add(Token("a"));
            if (!sameToken) db.MagicLinkTokens.Add(Token("b"));
            await db.SaveChangesAsync();
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new[] { "a", sameToken ? "a" : "b" }.Select(raw => Task.Run(async () => { await gate.Task; return await Verify(database, raw); })).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(tasks);
        Assert.Equal(sameToken ? 1 : 2, results.Count(result => result is not null));
        await using var check = Open(database);
        Assert.Equal(1, await check.AuthUsers.CountAsync());
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(60, true)]
    public async Task Expired_boundary_and_consumed_tokens_are_invalid(int seconds, bool consumed)
    {
        using var database = new TestDatabase();
        await using var db = Open(database);
        await db.Database.EnsureCreatedAsync();
        var token = Token("a", seconds);
        if (consumed) token.ConsumedAtUtc = Now.UtcDateTime;
        db.Add(token); await db.SaveChangesAsync();
        Assert.Null(await Verify(database, "a"));
    }

    [Fact]
    public async Task Save_failure_rolls_back_token_consumption()
    {
        using var database = new TestDatabase();
        await using var db = Open(database);
        await db.Database.EnsureCreatedAsync();
        db.Add(Token("a")); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<IOException>(() => Verify(database, "a", true));
        Assert.Null((await db.MagicLinkTokens.AsNoTracking().SingleAsync()).ConsumedAtUtc);
        Assert.NotNull(await Verify(database, "a"));
    }
}
