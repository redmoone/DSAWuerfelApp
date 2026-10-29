using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared.Models;
using DsaWuerfelApp.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DsaWuerfelApp.Tests;

public class StartupOwnershipTests
{
    [Fact]
    public void Test_host_persists_data_protection_keys_in_its_own_temporary_directory()
    {
        using var factory = new TestApplicationFactory();
        var services = factory.Services;
        var keyManagement = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        var repository = Assert.IsType<FileSystemXmlRepository>(keyManagement.XmlRepository);

        Assert.Equal(Path.GetFullPath(factory.Database.DataProtectionPath), repository.Directory.FullName);
        Assert.Equal("DsaWuerfelApp.Tests",
            services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);

        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("startup-test");
        var protectedValue = protector.Protect("test-value");

        Assert.Equal("test-value", protector.Unprotect(protectedValue));
        Assert.NotEmpty(Directory.GetFiles(factory.Database.DataProtectionPath, "key-*.xml"));
    }

    [Fact]
    public async Task Repeated_startup_preserves_orphan_and_owned_heroes_and_adds_index()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<HeroDbContext>().UseSqlite(database.ConnectionString).Options;
        var orphan = Guid.NewGuid(); var owned = Guid.NewGuid();
        await using (var db = new HeroDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.AuthUsers.AddRange(new AuthUser { Email = "a@example.test" }, new AuthUser { Email = "b@example.test" });
            db.Heroes.AddRange(new Hero { Id = orphan, SourceXml = System.Text.Encoding.UTF8.GetBytes("<daten><angaben><name>Reimportiert</name></angaben></daten>") }, new Hero { Id = owned, OwnerUserId = "owner", SourceXml = System.Text.Encoding.UTF8.GetBytes("<daten><angaben><name>Reimportiert</name></angaben></daten>") });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Heroes_OneActivePerOwner;");
        }
        for (var run = 0; run < 2; run++)
        {
            using var factory = new TestApplicationFactory(database);
            using var client = factory.CreateClient();
            await using var db = new HeroDbContext(options);
            Assert.Equal("", (await db.Heroes.SingleAsync(hero => hero.Id == orphan)).OwnerUserId);
            Assert.Equal("owner", (await db.Heroes.SingleAsync(hero => hero.Id == owned)).OwnerUserId);
            Assert.Equal(2, await db.Heroes.CountAsync());
            Assert.All(await db.Heroes.ToListAsync(), hero => { Assert.Equal("Reimportiert", hero.Name); Assert.Equal(3, hero.ImportVersion); });
            var indexes = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type='index'").ToListAsync();
            Assert.Contains("IX_Heroes_OneActivePerOwner", indexes);
        }
    }

    [Fact]
    public async Task Conflicting_legacy_database_fails_without_partial_schema_upgrade()
    {
        using var database = new TestDatabase();
        var options = new DbContextOptionsBuilder<HeroDbContext>().UseSqlite(database.ConnectionString).Options;
        await using (var db = new HeroDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IX_Heroes_OneActivePerOwner;");
            db.AddRange(new Hero { Id = Guid.NewGuid(), OwnerUserId = "owner", IsActive = true }, new Hero { Id = Guid.NewGuid(), OwnerUserId = "owner", IsActive = true });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Heroes DROP COLUMN SourceFileName;");
        }
        using var factory = new TestApplicationFactory(database);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("mehrere aktive Helden", error.ToString());
        await using var check = new HeroDbContext(options);
        Assert.Equal(2, await check.Heroes.CountAsync(hero => hero.IsActive));
        var columns = await check.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Heroes')").ToListAsync();
        Assert.DoesNotContain("SourceFileName", columns);
    }
}
