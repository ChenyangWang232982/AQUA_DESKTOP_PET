namespace AquaPet.PetHost.Action;

/// <summary>
/// Defines the left/right normal fall chains. A starts the fall, B continues
/// while airborne, and C starts immediately on landing.
/// </summary>
internal sealed record Fall(
    FallChain Left,
    FallChain Right)
{
    public static Fall FromDefaultAnimationDirectory(string defaultAnimationDirectory)
    {
        var assetRoot = Path.GetFullPath(
            Path.Combine(defaultAnimationDirectory, "..", "..", ".."));
        return new Fall(
            CreateChain(Path.Combine(assetRoot, "MOVE", "fall.left")),
            CreateChain(Path.Combine(assetRoot, "MOVE", "fall.right")));
    }

    private static FallChain CreateChain(string root) => new(
        Path.Combine(root, "A_Nomal"),
        Path.Combine(root, "B_Nomal"),
        Path.Combine(root, "C_Nomal"));
}

internal sealed record FallChain(
    string EnterAnimationDirectory,
    string HoldAnimationDirectory,
    string ExitAnimationDirectory);
