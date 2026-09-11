using System.Xml;

using DsaWuerfelApp.Core.Mappers;
using DsaWuerfelApp.Persistence;
using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Services.Application.Import;

public sealed class HeroCombatProfileReader(
    IHeroReadRepository heroReadRepository,
    XmlHeroDeserializer xmlHeroDeserializer,
    HeroCombatMapper heroCombatMapper)
{
    public async Task<CombatProfileDto?> ReadAsync(
        Guid heroId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var hero = await heroReadRepository.GetOwnedByIdAsync(heroId, ownerUserId, cancellationToken);
        if (hero is null)
        {
            return null;
        }

        if (hero.SourceXml is not { Length: > 0 })
        {
            throw new HeroCombatProfileException(
                $"Für den Helden „{hero.Name}“ fehlen gespeicherte Kampfdaten. Bitte die Heldendatei erneut importieren.");
        }

        try
        {
            using var stream = new MemoryStream(hero.SourceXml, writable: false);
            var source = xmlHeroDeserializer.DeserializeCombat(stream).FirstOrDefault();
            if (source is null)
            {
                throw new HeroCombatProfileException(
                    $"Für den Helden „{hero.Name}“ wurde keine unterstützte Kampfquelle gefunden.");
            }

            return heroCombatMapper.Map(hero, source);
        }
        catch (HeroCombatProfileException)
        {
            throw;
        }
        catch (XmlException exception)
        {
            throw new HeroCombatProfileException(
                $"Die gespeicherten Kampfdaten für „{hero.Name}“ konnten nicht gelesen werden.", exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new HeroCombatProfileException(
                $"Die gespeicherten Kampfdaten für „{hero.Name}“ konnten nicht abgebildet werden.", exception);
        }
    }
}

public sealed class HeroCombatProfileException(string message, Exception? innerException = null)
    : Exception(message, innerException);
