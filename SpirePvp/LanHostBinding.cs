using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace SpirePvp;

[HarmonyPatch(typeof(ENetHost), "StartHost")]
internal static class LanHostBinding
{
    private static readonly ConditionalWeakTable<INetGameService, object> OwnedServices = new();
    [ThreadStatic] private static bool _starting;
    [ThreadStatic] private static NetErrorInfo? _error;

    internal static NetErrorInfo? StartHost(NetHostGameService service, ushort port, int maxPlayers = 2)
    {
        if (maxPlayers != 2) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        _starting = true;
        _error = null;
        try
        {
            service.StartENetHost(port, maxPlayers);
            if (!_error.HasValue) Register(service);
            return _error;
        }
        finally
        {
            _starting = false;
            _error = null;
        }
    }

    internal static string ResolveBindAddress(string address) => _starting ? "*" : address;
    internal static void Register(INetGameService service) => OwnedServices.GetValue(service, _ => new object());
    internal static bool Owns(INetGameService service) => OwnedServices.TryGetValue(service, out _);

    internal static Error Bind(ENetConnection connection, string address, int port, int maxPeers,
        int maxChannels, int incomingBandwidth, int outgoingBandwidth)
    {
        return connection.CreateHostBound(ResolveBindAddress(address), port, maxPeers,
            maxChannels, incomingBandwidth, outgoingBandwidth);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var nativeBind = AccessTools.Method(typeof(ENetConnection), nameof(ENetConnection.CreateHostBound));
        var lanBind = AccessTools.Method(typeof(LanHostBinding), nameof(Bind));
        foreach (var instruction in instructions)
        {
            yield return instruction.Calls(nativeBind)
                ? new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = lanBind }
                : instruction;
        }
    }

    static void Postfix(NetErrorInfo? __result)
    {
        if (_starting) _error = __result;
    }
}

[HarmonyPatch]
internal static class LanLobbyCapacityPatch
{
    static MethodBase TargetMethod() => AccessTools.GetDeclaredConstructors(typeof(StartRunLobby))
        .Single(constructor => constructor.GetParameters() is { Length: 4 } parameters
            && parameters[1].ParameterType == typeof(INetGameService));

    static void Prefix(INetGameService netService, ref int maxPlayers)
    {
        if (LanHostBinding.Owns(netService)) maxPlayers = 2;
    }
}

[HarmonyPatch(typeof(StartRunLobby), "HandleClientLobbyJoinRequestMessage")]
internal static class LanLobbyIdentityPatch
{
    internal static ulong ExpectedClientId(IEnumerable<ModifierModel> modifiers) => modifiers.Any(modifier => modifier is PvpDuel)
        ? LanAddress.ClientId : LanAddress.NativeClientId;

    static bool Prefix(StartRunLobby __instance, ulong senderId)
    {
        if (!LanHostBinding.Owns(__instance.NetService) || senderId == ExpectedClientId(__instance.Modifiers)) return true;
        ((INetHostGameService)__instance.NetService).DisconnectClient(senderId, NetError.InvalidJoin);
        return false;
    }
}