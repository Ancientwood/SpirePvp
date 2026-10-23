using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;

namespace SpirePvp;

[HarmonyPatch(typeof(ENetHost), "StartHost")]
internal static class LanHostBinding
{
    [ThreadStatic] private static bool _starting;
    [ThreadStatic] private static NetErrorInfo? _error;

    internal static NetErrorInfo? StartHost(NetHostGameService service, ushort port, int maxPlayers = 2)
    {
        if (maxPlayers is < 2 or > 4) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
        _starting = true;
        _error = null;
        try
        {
            service.StartENetHost(port, maxPlayers);
            return _error;
        }
        finally
        {
            _starting = false;
            _error = null;
        }
    }

    internal static string ResolveBindAddress(string address) => _starting ? "*" : address;

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