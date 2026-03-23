using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Pages;

public partial class NotFound
{
    [Inject] public NavigationManager Nav { get; set; } = null!;
}