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

    public DbSet<AuthUser> AuthUsers => Set<AuthUser>();
    public DbSet<MagicLinkToken> MagicLinkTokens => Set<MagicLinkToken>();
    public DbSet<SessionRecord> SessionRecords => Set<SessionRecord>();
    public DbSet<SessionParticipantRecord> SessionParticipantRecords => Set<SessionParticipantRecord>();
    public DbSet<SessionRollHistoryRecord> SessionRollHistoryRecords => Set<SessionRollHistoryRecord>();
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
            entity.Property(hero => hero.SourceXml).HasColumnType("BLOB");
            entity.Property(hero => hero.SourceFileName).HasMaxLength(260);
            entity.Property(hero => hero.ImportVersion).HasDefaultValue(0);

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

        modelBuilder.Entity<AuthUser>(entity =>
        {
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(320);
            entity.Property(user => user.DisplayName).HasMaxLength(120);
            entity.HasIndex(user => user.Email).IsUnique();
        });

        modelBuilder.Entity<MagicLinkToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.Email).HasMaxLength(320);
            entity.Property(token => token.TokenHash).HasMaxLength(64);
            entity.Property(token => token.RedirectPath).HasMaxLength(512);
            entity.Property(token => token.RequestIp).HasMaxLength(128);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasIndex(token => new { token.Email, token.RequestedAtUtc });
        });

        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.ToTable("GameSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.Name).HasMaxLength(120);
            entity.Property(session => session.JoinCode).HasMaxLength(12);
            entity.Property(session => session.MasterUserId).HasMaxLength(64);
            entity.HasIndex(session => session.JoinCode).IsUnique();
            entity.HasIndex(session => session.MasterUserId);
            entity.HasMany(session => session.Participants)
                .WithOne(participant => participant.Session)
                .HasForeignKey(participant => participant.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionParticipantRecord>(entity =>
        {
            entity.ToTable("SessionParticipants");
            entity.HasKey(participant => new { participant.SessionId, participant.UserId });
            entity.Property(participant => participant.UserId).HasMaxLength(64);
            entity.Property(participant => participant.Name).HasMaxLength(120);
            entity.Property(participant => participant.AvatarUrl).HasMaxLength(512);
            entity.HasIndex(participant => participant.UserId);
        });

        modelBuilder.Entity<SessionRollHistoryRecord>(entity =>
        {
            entity.ToTable("SessionRollHistory");
            entity.HasKey(history => history.Id);
            entity.Property(history => history.PlayerName).HasMaxLength(120);
            entity.Property(history => history.RollsJson).HasColumnType("TEXT");
            entity.HasIndex(history => new { history.SessionId, history.TimestampUtc });
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