using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpirePvp;

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen._Process))]
internal static class DevelopmentProgressView
{
    static void Postfix(NMapScreen __instance)
    {
        var session = IndependentDevelopment.Current;
        session?.SendProgress();
        var icon = __instance.GetNodeOrNull<TextureRect>("SpirePvpOpponentProgress");
        if (session?.Developing != true || AccessTools.Field(typeof(NMapScreen), "_map").GetValue(__instance) is not PvpDevelopmentMap)
        {
            if (icon != null) icon.Visible = false;
            return;
        }
        if (session.OpponentRow < 0) return;
        var nodes = (Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(__instance)!;
        if (!nodes.TryGetValue(new MapCoord(3, session.OpponentRow), out var point)) return;
        if (icon == null)
        {
            icon = SceneHelper.Instantiate<TextureRect>("ui/multiplayer_vote_icon");
            icon.Name = "SpirePvpOpponentProgress";
            icon.Texture = session.OpponentCharacter.IconTexture;
            icon.GetNode<TextureRect>("Outline").Texture = session.OpponentCharacter.IconOutlineTexture;
            icon.MouseFilter = Control.MouseFilterEnum.Ignore;
            __instance.AddChild(icon);
        }
        icon.Visible = true;
        icon.GlobalPosition = point.GlobalPosition + new Vector2(point.Size.X * .5f - icon.Size.X * .5f, -icon.Size.Y * .7f);
    }
}
