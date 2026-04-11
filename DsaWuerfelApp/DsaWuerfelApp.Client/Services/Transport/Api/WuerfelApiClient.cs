using System.Net.Http.Json;

using DsaWuerfelApp.Shared;

namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelApiClient(HttpClient httpClient) : IWuerfelApiClient
{
    public Task<DicePageContextDto> GetContextAsync(Guid? heroId, CancellationToken cancellationToken = default)
    {
        var uri = heroId.HasValue
            ? $"api/dice/context?heroId={heroId.Value}"
            : "api/dice/context";

        return GetJsonAsync<DicePageContextDto>(uri, "Würfelkontext konnte nicht geladen werden.", cancellationToken);
    }

    public Task<ProbeInfoResultDto> GetProbeInfoAsync(
        ProbeInfoRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var parameters = new List<string>
        {
            $"probeValue={Uri.EscapeDataString(request.ProbeValue)}", $"modifier={request.Modifier}"
        };

        if (request.HeroId.HasValue)
        {
            parameters.Add($"heroId={request.HeroId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(request.BadTraitName))
        {
            parameters.Add($"badTraitName={Uri.EscapeDataString(request.BadTraitName)}");
        }

        foreach (var spellOptionValue in request.SpellOptionValues)
        {
            if (!string.IsNullOrWhiteSpace(spellOptionValue))
            {
                parameters.Add($"spellOptionValue={Uri.EscapeDataString(spellOptionValue)}");
            }
        }

        return GetJsonAsync<ProbeInfoResultDto>(
            $"api/dice/probe-info?{string.Join("&", parameters)}",
            "Probeninfo konnte nicht geladen werden.",
            cancellationToken);
    }

    public Task<FreeRollResultDto> RollFreeAsync(
        FreeRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<FreeRollResultDto>(
            "api/dice/free-roll",
            request,
            "Freier Wurf konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    public Task<TalentRollResultDto> RollTalentAsync(
        TalentRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<TalentRollResultDto>(
            "api/dice/talent-roll",
            request,
            "Probe konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    public Task<AttributeRollResultDto> RollAttributeAsync(
        AttributeRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<AttributeRollResultDto>(
            "api/dice/attribute-roll",
            request,
            "Eigenschaftsprobe konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    public Task<MasterTalentRollTargetResultDto[]> RollMasterTalentAsync(
        MasterTalentRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<MasterTalentRollTargetResultDto[]>(
            "api/dice/master-talent-roll",
            request,
            "Meister-Sammelwurf konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    public Task<MasterAttributeRollTargetResultDto[]> RollMasterAttributeAsync(
        MasterAttributeRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<MasterAttributeRollTargetResultDto[]>(
            "api/dice/master-attribute-roll",
            request,
            "Meister-Eigenschaftswurf konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    public Task<BadTraitRollResultDto> RollBadTraitAsync(
        BadTraitRollRequestDto request,
        CancellationToken cancellationToken = default)
    {
        return PostJsonAsync<BadTraitRollResultDto>(
            "api/dice/bad-trait-roll",
            request,
            "Probe auf schlechte Eigenschaft konnte nicht ausgeführt werden.",
            cancellationToken);
    }

    private async Task<T> GetJsonAsync<T>(string uri, string fallbackMessage, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        await response.EnsureApiSuccessAsync(fallbackMessage, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ??
               throw new InvalidOperationException(fallbackMessage);
    }

    private async Task<T> PostJsonAsync<T>(
        string uri,
        object request,
        string fallbackMessage,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(uri, request, cancellationToken);
        await response.EnsureApiSuccessAsync(fallbackMessage, cancellationToken);

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken) ??
               throw new InvalidOperationException(fallbackMessage);
    }
}
