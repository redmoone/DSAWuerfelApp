using DsaWuerfelApp.Client.Pages;

using Microsoft.AspNetCore.Components;

namespace DsaWuerfelApp.Client.Components;

public partial class RollEquation
{
    [Parameter] public Wuerfel.RollSetResult? Result { get; set; }

    private string EquationText
    {
        get
        {
            if (Result is null || !Result.Rolls.Any())
                return string.Empty;

            var rolls = string.Join(" + ", Result.Rolls.Select(r => r.Value));

            return Result.Modifier switch
            {
                > 0 => $"{rolls} + {Result.Modifier}",
                < 0 => $"{rolls} - {Math.Abs(Result.Modifier)}",
                _ => rolls
            };
        }
    }
}