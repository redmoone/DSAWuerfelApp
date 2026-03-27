using System.Text.Json;

using DsaWuerfelApp.Shared.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace DsaWuerfelApp.Persistence;

public class HeroDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public HeroDbContext(DbContextOptions<HeroDbContext> options) : base(options)
    {
    }

    public DbSet<Hero> Heroes => Set<Hero>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dictionaryConverter = new ValueConverter<Dictionary<string, int>, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<Dictionary<string, int>>(value, JsonOptions) ??
                     new Dictionary<string, int>());

        var dictionaryComparer = new ValueComparer<Dictionary<string, int>>(
            (left, right) => SerializeDictionary(left) == SerializeDictionary(right),
            value => SerializeDictionary(value).GetHashCode(),
            value => JsonSerializer.Deserialize<Dictionary<string, int>>(SerializeDictionary(value), JsonOptions) ??
                     new Dictionary<string, int>());

        modelBuilder.Entity<Hero>(entity =>
        {
            entity.HasKey(hero => hero.Id);
            entity.Property(hero => hero.IsActive).HasDefaultValue(false);
            entity.Property(hero => hero.Name).HasMaxLength(200);
            entity.Property(hero => hero.Geschlecht).HasMaxLength(100);

            entity.Property(hero => hero.Eigenschaften)
                .HasConversion(dictionaryConverter)
                .Metadata.SetValueComparer(dictionaryComparer);

            entity.Property(hero => hero.Talente)
                .HasConversion(dictionaryConverter)
                .Metadata.SetValueComparer(dictionaryComparer);
        });
    }

    private static string SerializeDictionary(Dictionary<string, int>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, int>(), JsonOptions);
    }
}