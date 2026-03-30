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
        var attributeDictionaryConverter = new ValueConverter<Dictionary<string, int>, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            value => JsonSerializer.Deserialize<Dictionary<string, int>>(value, JsonOptions) ??
                     new Dictionary<string, int>());

        var attributeDictionaryComparer = new ValueComparer<Dictionary<string, int>>(
            (left, right) => SerializeDictionary(left) == SerializeDictionary(right),
            value => SerializeDictionary(value).GetHashCode(),
            value => JsonSerializer.Deserialize<Dictionary<string, int>>(SerializeDictionary(value), JsonOptions) ??
                     new Dictionary<string, int>());

        var talentDictionaryConverter = new ValueConverter<Dictionary<string, TalentData>, string>(
            value => SerializeTalentDictionary(value),
            value => DeserializeTalentDictionary(value));

        var talentDictionaryComparer = new ValueComparer<Dictionary<string, TalentData>>(
            (left, right) => SerializeTalentDictionary(left) == SerializeTalentDictionary(right),
            value => SerializeTalentDictionary(value).GetHashCode(),
            value => DeserializeTalentDictionary(SerializeTalentDictionary(value)));

        modelBuilder.Entity<Hero>(entity =>
        {
            entity.HasKey(hero => hero.Id);
            entity.Property(hero => hero.IsActive).HasDefaultValue(false);
            entity.Property(hero => hero.Name).HasMaxLength(200);
            entity.Property(hero => hero.Geschlecht).HasMaxLength(100);

            entity.Property(hero => hero.Eigenschaften)
                .HasConversion(attributeDictionaryConverter)
                .Metadata.SetValueComparer(attributeDictionaryComparer);

            entity.Property(hero => hero.SchlechteEigenschaften)
                .HasConversion(attributeDictionaryConverter)
                .Metadata.SetValueComparer(attributeDictionaryComparer);

            entity.Property(hero => hero.Talente)
                .HasConversion(talentDictionaryConverter)
                .Metadata.SetValueComparer(talentDictionaryComparer);
        });
    }

    private static string SerializeDictionary(Dictionary<string, int>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, int>(), JsonOptions);
    }

    private static string SerializeTalentDictionary(Dictionary<string, TalentData>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, TalentData>(), JsonOptions);
    }

    private static Dictionary<string, TalentData> DeserializeTalentDictionary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new Dictionary<string, TalentData>();
        }

        using var document = JsonDocument.Parse(value);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, TalentData>();
        }

        var talents = new Dictionary<string, TalentData>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            talents[property.Name] = DeserializeTalent(property.Value);
        }

        return talents;
    }

    private static TalentData DeserializeTalent(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => new TalentData { Wert = element.GetInt32() },
            JsonValueKind.Object => JsonSerializer.Deserialize<TalentData>(element.GetRawText(), JsonOptions) ??
                                    new TalentData(),
            _ => new TalentData()
        };
    }
}