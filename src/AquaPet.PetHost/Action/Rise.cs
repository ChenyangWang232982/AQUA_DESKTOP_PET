namespace AquaPet.PetHost.Action;

/// <summary>
/// Defines the art and timing for the pet's raised (drag-held) action.
/// </summary>
internal sealed record Rise(
    string EnterAnimationDirectory,
    string HoldAnimationDirectory)
{
    public static Rise FromDefaultAnimationDirectory(string defaultAnimationDirectory)
    {
        var assetRoot = Path.GetFullPath(
            Path.Combine(defaultAnimationDirectory, "..", "..", ".."));
        var staticRaiseRoot = Path.Combine(assetRoot, "Raise", "Raised_Static");
        return new Rise(
            Path.Combine(staticRaiseRoot, "A_Nomal"),
            Path.Combine(staticRaiseRoot, "B_Nomal"));
    }
}
