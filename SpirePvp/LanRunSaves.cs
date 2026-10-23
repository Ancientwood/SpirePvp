using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace SpirePvp;

internal static class LanRunSaves
{
    internal const string NativeFile = "spirepvp_native_a1_a2.save";
    internal const string PvpFile = "spirepvp_duel_lan.save";
    private static readonly AsyncLocal<string?> ScopedFile = new();
    internal static string? CurrentFile => ScopedFile.Value;
    internal static string? ActiveFile { get; private set; }

    internal sealed class Scope : IDisposable
    {
        private readonly string? _previous = ScopedFile.Value;
        internal Scope(string file) => ScopedFile.Value = file;
        public void Dispose() => ScopedFile.Value = _previous;
    }

    private static RunSaveManager Manager => (RunSaveManager)AccessTools.Field(typeof(SaveManager), "_runSaveManager")
        .GetValue(SaveManager.Instance)!;

    internal static string FileFor(bool native) => native ? NativeFile : PvpFile;
    internal static void Activate(bool native) => ActiveFile = FileFor(native);
    internal static void ClearActive() => ActiveFile = null;

    internal static bool IsCompatible(SerializableRun save, bool native)
    {
        if (save.PlatformType != PlatformType.None || save.Players.Count != 2) return false;
        ulong clientId = native ? LanAddress.NativeClientId : LanAddress.ClientId;
        return save.Players.Select(player => player.NetId).ToHashSet().SetEquals([LanAddress.HostId, clientId])
            && save.Modifiers.Any(modifier => modifier.Id == ModelDb.Modifier<PvpDuel>().Id) != native;
    }

    internal static bool HasSave(bool native)
    {
        using var scope = new Scope(FileFor(native));
        return Manager.HasMultiplayerRunSave;
    }

    internal static ReadSaveResult<SerializableRun> Load(bool native)
    {
        using var scope = new Scope(FileFor(native));
        var result = Manager.LoadMultiplayerRunSave();
        if (!result.Success || result.SaveData == null) return result;
        if (!IsCompatible(result.SaveData, native))
            return new ReadSaveResult<SerializableRun>(ReadSaveStatus.ValidationFailed,
                "The LAN save does not match this two-player room and its fixed identities.");
        try
        {
            return new ReadSaveResult<SerializableRun>(RunManager.CanonicalizeSave(result.SaveData, LanAddress.HostId),
                ReadSaveStatus.Success);
        }
        catch (Exception exception)
        {
            Log.Warn("[SpirePvp] LAN save validation failed; the original file was kept: " + exception.Message);
            return new ReadSaveResult<SerializableRun>(ReadSaveStatus.ValidationFailed, exception.Message);
        }
    }
}

[HarmonyPatch(typeof(RunSaveManager), "CurrentMultiplayerRunSavePath", MethodType.Getter)]
internal static class LanSavePathPatch
{
    static void Postfix(ref string __result)
    {
        if (LanRunSaves.CurrentFile is { } file) __result = __result.GetBaseDir().PathJoin(file);
    }
}

[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun), [typeof(SerializableRun), typeof(bool)])]
internal static class LanSaveWritePatch
{
    static void Prefix(SerializableRun save, bool isMultiplayer, out LanRunSaves.Scope? __state)
    {
        __state = null;
        if (!isMultiplayer) return;
        if (LanRunSaves.IsCompatible(save, native: true)) __state = new LanRunSaves.Scope(LanRunSaves.NativeFile);
        else if (LanRunSaves.IsCompatible(save, native: false)) __state = new LanRunSaves.Scope(LanRunSaves.PvpFile);
        else if (LanRunSaves.ActiveFile != null && save.PlatformType == PlatformType.None)
            throw new InvalidDataException("Refusing to overwrite another save with an incompatible LAN snapshot.");
    }

    static void Finalizer(LanRunSaves.Scope? __state) => __state?.Dispose();
}

[HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.DeleteCurrentMultiplayerRun))]
internal static class LanSaveDeletePatch
{
    static void Prefix(out LanRunSaves.Scope? __state)
    {
        __state = null;
        if (LanRunSaves.ActiveFile is { } file && RunManager.Instance.NetService.Platform == PlatformType.None
            && RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
            __state = new LanRunSaves.Scope(file);
    }

    static void Finalizer(LanRunSaves.Scope? __state) => __state?.Dispose();
}