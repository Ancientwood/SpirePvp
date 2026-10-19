using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Logging;

namespace SpirePvp;

internal static class DuelMapIcon
{
    private static Texture2D? _texture;
    private static readonly ConditionalWeakTable<NBossMapPoint, TextureRect> Images = new();

    // Embedded bytes avoid external file paths, Godot imports and extra PCKs.
    // Only execute inside Godot, never during model discovery or offline tests.
    internal static Texture2D Texture
    {
        get
        {
            if (_texture != null && GodotObject.IsInstanceValid(_texture)) return _texture;
            using var stream = typeof(ModEntry).Assembly.GetManifestResourceStream("SpirePvp.Assets.Map.DuelIcon.png")
                ?? throw new InvalidOperationException("缺少 PvP 地图图标资源。");
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            using var image = new Image();
            if (image.LoadPngFromBuffer(bytes.ToArray()) != Error.Ok)
                throw new InvalidOperationException("PvP 地图图标 PNG 解码失败。");
            image.GenerateMipmaps();
            _texture = ImageTexture.CreateFromImage(image);
            return _texture;
        }
    }

    internal static void Apply(NBossMapPoint boss)
    {
        var image = boss.GetNode<TextureRect>("%PlaceholderImage");
        var outline = boss.GetNode<TextureRect>("%PlaceholderOutline");
        boss.GetNode<Node2D>("%SpineSprite").Visible = false;
        // Populate both fields even when native _Ready chose an animated boss.
        AccessTools.Field(typeof(NBossMapPoint), "_placeholderImage").SetValue(boss, image);
        AccessTools.Field(typeof(NBossMapPoint), "_placeholderOutline").SetValue(boss, outline);
        AccessTools.Field(typeof(NBossMapPoint), "_usesSpine").SetValue(boss, false);
        image.Texture = Texture;
        image.Material = null;
        image.TextureFilter = CanvasItem.TextureFilterEnum.LinearWithMipmaps;
        image.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        image.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        image.Visible = true;
        outline.Visible = false;
        outline.Texture = null;
        Images.Remove(boss);
        Images.Add(boss, image);
        Refresh(boss);
    }

    internal static bool Refresh(NBossMapPoint boss)
    {
        if (!Images.TryGetValue(boss, out var image)) return false;
        // Native placeholder colors multiply a monochrome mask. Our PNG already
        // contains colors: preserve the flame and sword palette instead of tinting.
        image.SelfModulate = new Color(1, 1, 1, boss.State == MapPointState.Untravelable ? .55f : 1f);
        return true;
    }
}

[HarmonyPatch(typeof(NBossMapPoint), nameof(NBossMapPoint._Ready))]
internal static class DuelBossIconPatch
{
    static void Postfix(NBossMapPoint __instance)
    {
        if (AccessTools.Field(typeof(NMapPoint), "_runState").GetValue(__instance) is IRunState run && run.Map is DuelMap or PvpDevelopmentMap)
            DuelMapIcon.Apply(__instance);
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class DuelIconPreloadPatch
{
    static void Postfix()
    {
        var texture = DuelMapIcon.Texture;
        Log.Info($"[SpirePvp] PvP 地图图标已解码：{texture.GetWidth()}×{texture.GetHeight()}。");
    }
}

[HarmonyPatch(typeof(NBossMapPoint), "RefreshColorInstantly")]
internal static class DuelBossIconColorPatch
{
    static bool Prefix(NBossMapPoint __instance) => !DuelMapIcon.Refresh(__instance);
}
