namespace DsaWuerfelApp.Client.Services;

public sealed class WuerfelSelectionService(WuerfelState state)
{
    public void AddDie(int sides)
    {
        state.SwitchArea(WuerfelArea.FreeRoll);
        var dice = state.Current.SelectedDiceSides.ToList();
        dice.Add(sides);
        state.SetSelectedDice(dice);
    }

    public void RemoveDie(int index)
    {
        var dice = state.Current.SelectedDiceSides.ToList();
        if (index < 0 || index >= dice.Count)
        {
            return;
        }

        dice.RemoveAt(index);
        var attributes = state.Current.SelectedAttributes.ToList();
        if (dice.Count < attributes.Count)
        {
            attributes.RemoveAt(attributes.Count - 1);
        }

        state.SetSelectedAttributes(attributes, dice);
    }

    public void AddAttribute(string shortName)
    {
        state.SwitchArea(WuerfelArea.Attributes);

        var attributes = state.Current.SelectedAttributes.ToList();
        var dice = state.Current.SelectedDiceSides.ToList();
        var attributeCount =
            attributes.Count(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

        if (attributeCount >= 3)
        {
            attributes.RemoveAll(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

            for (var index = 0; index < 3; index++)
            {
                var dieIndex = dice.IndexOf(20);
                if (dieIndex >= 0)
                {
                    dice.RemoveAt(dieIndex);
                }
            }

            state.SetSelectedAttributes(attributes, dice);
            return;
        }

        if (attributes.Count < 3)
        {
            attributes.Add(shortName);
            dice.Add(20);
            state.SetSelectedAttributes(attributes, dice);
            return;
        }

        attributes.RemoveAt(0);
        attributes.Add(shortName);
        state.SetSelectedAttributes(attributes, dice);
    }

    public void RemoveAttribute(string shortName)
    {
        state.SwitchArea(WuerfelArea.Attributes);

        var attributes = state.Current.SelectedAttributes.ToList();
        var dice = state.Current.SelectedDiceSides.ToList();
        var attributeCount =
            attributes.Count(attribute => string.Equals(attribute, shortName, StringComparison.Ordinal));

        if (attributeCount == 0)
        {
            attributes.Clear();
            dice.Clear();
            attributes.AddRange([shortName, shortName, shortName]);
            dice.AddRange([20, 20, 20]);
            state.SetSelectedAttributes(attributes, dice);
            return;
        }

        var attributeIndex = attributes.LastIndexOf(shortName);
        if (attributeIndex >= 0)
        {
            attributes.RemoveAt(attributeIndex);
        }

        var dieIndexToRemove = dice.LastIndexOf(20);
        if (dieIndexToRemove >= 0)
        {
            dice.RemoveAt(dieIndexToRemove);
        }

        state.SetSelectedAttributes(attributes, dice);
    }

    public void SetSelectedProbe(string? selectedProbeValue)
    {
        if (!string.IsNullOrWhiteSpace(selectedProbeValue))
        {
            state.SwitchArea(WuerfelArea.ProbeSearch);
            state.SetSelectedProbe(selectedProbeValue);
            return;
        }

        state.SetSelectedProbe(null);
    }

    public void SetSelectedBadTrait(string? selectedBadTraitName)
    {
        state.SetSelectedBadTrait(selectedBadTraitName);
    }

    public void SetModifier(int modifier)
    {
        state.SetModifier(modifier);
    }

    public void ToggleHiddenRoll()
    {
        state.ToggleHiddenRoll();
    }

    public void SetForcedRollsText(string forcedRollsText)
    {
        state.SetForcedRollsText(forcedRollsText);
    }

    public void SetForcedRollPreset(string forcedRollsText)
    {
        state.SetForcedRollsText(forcedRollsText);
    }

    public void ClearForcedRolls()
    {
        state.SetForcedRollsText(string.Empty);
    }

    public void Reset()
    {
        state.ResetRollArea();
    }
}