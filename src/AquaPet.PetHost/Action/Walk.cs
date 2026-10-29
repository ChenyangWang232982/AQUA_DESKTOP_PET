namespace AquaPet.PetHost.Action;

internal enum WalkDirection
{
    Left,
    Right
}

/// <summary>
/// Defines the normal walk chains. Call <see cref="GetChain"/> with Left or
/// Right, then play A once, loop B while moving, and play C once when stopping.
/// </summary>
internal sealed record Walk(WalkChain Left, WalkChain Right)
{
    public WalkChain GetChain(WalkDirection direction) =>
        direction == WalkDirection.Left ? Left : Right;

    public static Walk FromDefaultAnimationDirectory(string defaultAnimationDirectory)
    {
        var assetRoot = Path.GetFullPath(
            Path.Combine(defaultAnimationDirectory, "..", "..", ".."));
        return new Walk(
            CreateChain(Path.Combine(assetRoot, "MOVE", "walk.left")),
            CreateChain(Path.Combine(assetRoot, "MOVE", "walk.right")));
    }

    private static WalkChain CreateChain(string root) => new(
        Path.Combine(root, "A_Nomal"),
        Path.Combine(root, "B_Nomal"),
        Path.Combine(root, "C_Nomal"));
}

internal sealed record WalkChain(
    string EnterAnimationDirectory,
    string HoldAnimationDirectory,
    string ExitAnimationDirectory);
