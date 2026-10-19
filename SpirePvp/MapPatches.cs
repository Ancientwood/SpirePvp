using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpirePvp;

[HarmonyPatch(typeof(RunManager), nameof(RunManager.GenerateMap))]
internal static class DuelMapGenerationPatch
{
    static bool Prefix(RunManager __instance, ref Task __result)
    {
        var state = PvpSession.GetState(__instance);
        if (!PvpSession.IsPvp(state)) return true;
        __instance.MapSelectionSynchronizer.BeforeMapGenerated();
        state!.Map = state.Modifiers.OfType<PvpDuel>().Single().Development
            ? new PvpDevelopmentMap(state.Rng.Seed)
            : new DuelMap();
        state.RemoveStaleVisitedMapCoords(state.Map);
        NMapScreen.Instance?.SetMap(state.Map, state.Rng.Seed, clearDrawings: true);
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(RunManager), "CreateRoom")]
internal static class DuelRoomPatch
{
    static bool Prefix(RunManager __instance, RoomType roomType, ref AbstractRoom __result)
    {
        var state = PvpSession.GetState(__instance);
        if (!PvpSession.IsPvp(state) || roomType != RoomType.Boss) return true;
        __result = new CombatRoom(ModelDb.Encounter<PvpArena>().ToMutable(), state!);
        return false;
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetMap))]
internal static class DuelMapAppearancePatch
{
    static void Postfix(NMapScreen __instance, ActMap map)
    {
        if (map is not (DuelMap or PvpDevelopmentMap)) return;
        if (AccessTools.Field(typeof(NMapScreen), "_startingPointNode").GetValue(__instance) is Control start)
            start.Visible = false;
        AccessTools.Field(typeof(NMapScreen), "_hasPlayedAnimation").SetValue(__instance, true);
        if (map is DuelMap) DuelMapView.Position(__instance);
    }
}

internal static class DuelMapView
{
    internal static bool IsDuel(NMapScreen screen) => AccessTools.Field(typeof(NMapScreen), "_map").GetValue(screen) is DuelMap;
    internal static bool IsPvpMap(NMapScreen screen) => AccessTools.Field(typeof(NMapScreen), "_map").GetValue(screen) is DuelMap or PvpDevelopmentMap;
    internal static void Position(NMapScreen screen)
    {
        if (AccessTools.Field(typeof(NMapScreen), "_bossPointNode").GetValue(screen) is not Control boss) return;
        // Set the initial viewport position only. The native map container must
        // remain free to move this node when scrolling or dragging the map.
        var center = screen.GetGlobalTransform() * (screen.Size * new Vector2(.49f, .23f));
        boss.GlobalPosition = center - boss.GetGlobalTransform().BasisXform(boss.Size * .5f);
    }
    internal static void UpdateStatus(NMapScreen screen)
    {
        var label = screen.GetNodeOrNull<Label>("SpirePvpMapStatus");
        if (label == null)
        {
            label = new Label
            {
                Name = "SpirePvpMapStatus", MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            label.AddThemeFontSizeOverride("font_size", 26);
            screen.AddChild(label);
        }
        label.Position = new Vector2(screen.Size.X * .16f, screen.Size.Y * .72f);
        label.Size = new Vector2(screen.Size.X * .6f, 100);
        label.Text = IndependentDevelopment.Current?.Waiting == true
            ? "已完成发育，等待对手到达决斗房间…"
            : PvpSession.SelectingDecks
            ? "构筑已完成，等待对手完成选牌与同步…"
            : "双方构筑已完成，请各自点击决斗图标进入战斗";
        label.Visible = IndependentDevelopment.Current?.Waiting == true
            || (IsDuel(screen) && !PvpSession.SelectingDecks) || (PvpSession.SelectingDecks && PvpSession.LocalDeckReady);
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
internal static class CenterDuelMapOnOpenPatch
{
    static void Postfix(NMapScreen __instance)
    {
        if (DuelMapView.IsPvpMap(__instance))
        {
            if (DuelMapView.IsDuel(__instance)) DuelMapView.Position(__instance);
            DuelMapView.UpdateStatus(__instance);
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
internal static class DuelMapTravelPatch
{
    static void Postfix(NMapScreen __instance)
    {
        if (!DuelMapView.IsPvpMap(__instance)) return;
        if (AccessTools.Field(typeof(NMapScreen), "_startingPointNode").GetValue(__instance) is NMapPoint start)
            start.State = MapPointState.Untravelable;
        if (AccessTools.Field(typeof(NMapScreen), "_map").GetValue(__instance) is PvpDevelopmentMap development)
        {
            var run = PvpSession.GetState(RunManager.Instance)!;
            var next = development.GetNextPoint(run.VisitedMapCoords);
            var nodes = (Dictionary<MapCoord, NMapPoint>)AccessTools.Field(typeof(NMapScreen), "_mapPointDictionary").GetValue(__instance)!;
            foreach (var point in development.GetAllMapPoints().Append(development.BossMapPoint))
            {
                if (!nodes.TryGetValue(point.coord, out var node)) continue;
                node.State = run.VisitedMapCoords.Contains(point.coord) ? MapPointState.Traveled
                    : !PvpSession.SelectingDecks && point == next ? MapPointState.Travelable : MapPointState.Untravelable;
            }
            return;
        }
        if (AccessTools.Field(typeof(NMapScreen), "_bossPointNode").GetValue(__instance) is NMapPoint boss)
        {
            var state = PvpSession.GetState(RunManager.Instance)!;
            boss.State = state.VisitedMapCoords.Contains(boss.Point.coord) ? MapPointState.Traveled
                : PvpSession.SelectingDecks ? MapPointState.Untravelable : MapPointState.Travelable;
        }
    }
}

[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.SetTravelEnabled))]
internal static class LockDraftMapPatch
{
    static void Prefix(NMapScreen __instance, ref bool enabled)
    {
        if ((PvpSession.SelectingDecks || IndependentDevelopment.Current?.Waiting == true)
            && PvpSession.IsPvp(PvpSession.GetState(RunManager.Instance))) enabled = false;
    }
    static void Postfix(NMapScreen __instance)
    {
        if (!DuelMapView.IsPvpMap(__instance)) return;
        // Open() returns early if the waiting map is already open. Refresh the
        // node state explicitly when travel unlocks, not just the global flag.
        AccessTools.Method(typeof(NMapScreen), "RecalculateTravelability").Invoke(__instance, null);
        __instance.RefreshAllPointVisuals();
        DuelMapView.UpdateStatus(__instance);
    }
}

// Question marks in the development route always open a native event. An
// event can still offer its own combat; that combat uses normal co-op rules.
[HarmonyPatch(typeof(RunManager), "RollRoomTypeFor")]
internal static class DevelopmentEventRoomPatch
{
    static bool Prefix(RunManager __instance, MapPointType pointType, ref RoomType __result)
    {
        if (PvpSession.GetState(__instance)?.Map is not PvpDevelopmentMap || pointType != MapPointType.Unknown) return true;
        __result = RoomType.Event;
        return false;
    }
}
