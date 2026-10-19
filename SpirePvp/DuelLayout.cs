namespace SpirePvp;

public readonly record struct DuelPlacement(float X, float Y, bool Mirror);

public static class DuelLayout
{
    // Viewer-relative, independent of the host or player-slot order.
    public static DuelPlacement Place(ulong viewer, ulong owner, int petIndex = -1)
    {
        var own = viewer == owner;
        var x = own ? .25f : .75f;
        if (petIndex < 0) return new(x, .67f, !own);
        var direction = own ? 1f : -1f;
        var column = petIndex % 2;
        var row = petIndex / 2;
        return new(x + direction * (.085f + column * .065f), .69f - row * .065f, !own);
    }
    public static float MirrorAnchorX(float original, float center, bool mirror) => mirror ? 2 * center - original : original;
}
