using System.Text.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services;

public sealed class CombatEnemyCatalogStore(IHostEnvironment environment)
{
    public const string CatalogFileName = "meister-gegner-dsa41.json";
    public const string CatalogId = "dsa41-gm-enemy-catalog";
    public const int SchemaVersion = 1;
    public const int ExpectedEnemyCount = 25;

    private readonly Lazy<CombatEnemyCatalogDto> _catalog =
        new(() => LoadCatalog(ResolveCatalogPath(environment)));

    public CombatEnemyCatalogDto Catalog => _catalog.Value;

    public IReadOnlyList<CombatEnemyCatalogEntryDto> Entries => Catalog.Enemies;

    public bool TryGetEntry(string id, out CombatEnemyCatalogEntryDto entry)
    {
        entry = Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, id, StringComparison.Ordinal))!;
        return entry is not null;
    }

    private static string ResolveCatalogPath(IHostEnvironment environment)
    {
        var candidatePaths = new[]
        {
            Path.Combine(environment.ContentRootPath, "Data", CatalogFileName),
            Path.Combine(AppContext.BaseDirectory, "Data", CatalogFileName)
        };

        return candidatePaths.FirstOrDefault(File.Exists) ??
               throw new FileNotFoundException(
                   $"Der Gegnerkatalog '{CatalogFileName}' wurde nicht gefunden. Erwartete Pfade: " +
                   string.Join("; ", candidatePaths),
                   candidatePaths[0]);
    }

    private static CombatEnemyCatalogDto LoadCatalog(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var catalog = JsonSerializer.Deserialize<CombatEnemyCatalogDto>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            }) ?? throw new InvalidDataException(
                $"Der Gegnerkatalog '{path}' ist leer.");

            ValidateCatalog(catalog, path);
            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Der Gegnerkatalog '{path}' ist kein gültiges JSON im erwarteten DSA-4.1-Format.",
                exception);
        }
    }

    private static void ValidateCatalog(CombatEnemyCatalogDto catalog, string path)
    {
        if (!string.Equals(catalog.Format.Id, CatalogId, StringComparison.Ordinal) ||
            catalog.Format.SchemaVersion != SchemaVersion)
        {
            throw InvalidCatalog(path, "Format-ID oder Schema-Version stimmt nicht.");
        }

        if (!string.Equals(catalog.Ruleset.Name, "DSA 4.1", StringComparison.Ordinal) ||
            !string.Equals(catalog.Ruleset.Edition, "4.1", StringComparison.Ordinal) ||
            !catalog.Ruleset.Strict ||
            !catalog.Ruleset.DoNotMixWith.Contains("DSA 5", StringComparer.Ordinal))
        {
            throw InvalidCatalog(path, "Der Katalog ist nicht als strikter DSA-4.1-Katalog markiert.");
        }

        if (catalog.Sources.Length == 0 || catalog.SpecialRuleDefinitions.Length == 0)
        {
            throw InvalidCatalog(path, "Quellen oder Sonderregeldefinitionen fehlen.");
        }

        if (catalog.Enemies.Length != ExpectedEnemyCount)
        {
            throw InvalidCatalog(path,
                $"Es werden genau {ExpectedEnemyCount} Gegnerprofile erwartet, gefunden wurden {catalog.Enemies.Length}.");
        }

        var sourceIds = catalog.Sources
            .Select(source => source.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var enemyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var enemy in catalog.Enemies)
        {
            if (string.IsNullOrWhiteSpace(enemy.Id) ||
                string.IsNullOrWhiteSpace(enemy.Name) ||
                !enemyIds.Add(enemy.Id))
            {
                throw InvalidCatalog(path, "Gegner-IDs müssen vorhanden und eindeutig sein.");
            }

            if (enemy.SourceRefs.Length == 0 ||
                enemy.SourceRefs.Any(sourceRef => !sourceIds.Contains(sourceRef.SourceId)))
            {
                throw InvalidCatalog(path, $"Gegner '{enemy.Id}' verweist auf keine gültige Quelle.");
            }

            if (enemy.Readiness is not ("ready" or "sourceDependent" or "requiresEquipmentSelection") ||
                (enemy.CombatReady && enemy.Readiness != "ready") ||
                (!enemy.CombatReady && enemy.Readiness == "ready"))
            {
                throw InvalidCatalog(path, $"Kampfbereitschaft von '{enemy.Id}' ist widersprüchlich.");
            }

            var variantIds = new HashSet<string>(StringComparer.Ordinal);
            if (enemy.CombatVariants.Any(variant =>
                    string.IsNullOrWhiteSpace(variant.Id) || !variantIds.Add(variant.Id)))
            {
                throw InvalidCatalog(path, $"Erfahrungsvarianten von '{enemy.Id}' sind nicht eindeutig.");
            }

            var attackIds = new HashSet<string>(StringComparer.Ordinal);
            if (enemy.Attacks.Any(attack =>
                    string.IsNullOrWhiteSpace(attack.Id) || !attackIds.Add(attack.Id)))
            {
                throw InvalidCatalog(path, $"Angriffs-IDs von '{enemy.Id}' sind nicht eindeutig.");
            }

            if (enemy.Readiness == "requiresEquipmentSelection" &&
                (enemy.Equipment?.SelectionRequired != true || enemy.CombatVariants.Length == 0))
            {
                throw InvalidCatalog(path,
                    $"Ausrüstungsabhängiger Gegner '{enemy.Id}' hat keine vollständige Auswahlgrenze.");
            }

            if (enemy.Combat?.Resources.LeP.SourceRange is { } range &&
                (!range.Min.HasValue || !range.Max.HasValue || range.Min > range.Max))
            {
                throw InvalidCatalog(path, $"LeP-Quellenbereich von '{enemy.Id}' ist ungültig.");
            }
        }
    }

    private static InvalidDataException InvalidCatalog(string path, string reason) =>
        new($"Der Gegnerkatalog '{path}' ist ungültig: {reason}");
}
