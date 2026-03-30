using DsaWuerfelApp.Shared;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class RollHistory
{
    [Parameter] public IReadOnlyList<RollHistoryEntryDto> Entries { get; set; } = Array.Empty<RollHistoryEntryDto>();
}