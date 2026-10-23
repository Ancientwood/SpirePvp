using SpirePvp;

internal static class LanConnectionTests
{
#if !LAN_ONLY
    internal static void RunGameChecks(Action<bool, string> check)
    {
        var settings = new PvpSettings { LanPort = 45000, LastLanAddress = "192.168.1.2:45000" };
        string json = System.Text.Json.JsonSerializer.Serialize(settings);
        var restored = System.Text.Json.JsonSerializer.Deserialize<PvpSettings>(json)!;
        check(restored.LanPort == 45000 && restored.LastLanAddress == settings.LastLanAddress,
            "LAN settings round trip without changing PvP rules");
        settings.LanPort = 0;
        check(settings.LanPort == LanAddress.DefaultPort, "Invalid saved LAN port uses the default");
        settings.LanPort = 65536;
        check(settings.LanPort == LanAddress.DefaultPort, "Saved LAN port cannot overflow ushort");
        var legacy = System.Text.Json.JsonSerializer.Deserialize<PvpSettings>("{\"SameSeed\":true}")!;
        check(legacy.SameSeed && legacy.LanPort == LanAddress.DefaultPort && legacy.LastLanAddress == string.Empty,
            "Existing settings files retain their PvP options and get LAN defaults");

        var patch = typeof(ModEntry).Assembly.GetType("SpirePvp.LanHostBinding", throwOnError: true)!;
        var resolve = HarmonyLib.AccessTools.Method(patch, "ResolveBindAddress");
        var starting = HarmonyLib.AccessTools.Field(patch, "_starting");
        check((string)resolve.Invoke(null, ["127.0.0.1"])! == "127.0.0.1",
            "Non-LAN ENet hosting retains its native bind address");
        try
        {
            starting.SetValue(null, true);
            check((string)resolve.Invoke(null, ["127.0.0.1"])! == "*",
                "Explicit PvP LAN hosting listens on network interfaces");
        }
        finally { starting.SetValue(null, false); }
        check((string)resolve.Invoke(null, ["127.0.0.1"])! == "127.0.0.1",
            "LAN binding does not leak into later ENet hosts");

        var host = HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetHost), "StartHost");
        check(HarmonyLib.Harmony.GetPatchInfo(host)?.Transpilers.Any(info => info.owner == "spirepvp.duel") == true,
            "LAN bind patch is registered on the installed game's ENet host");
        var native = HarmonyLib.PatchProcessor.GetOriginalInstructions(host);
        var rewritten = (IEnumerable<HarmonyLib.CodeInstruction>)HarmonyLib.AccessTools.Method(patch, "Transpiler")
            .Invoke(null, [native])!;
        var bind = HarmonyLib.AccessTools.Method(patch, "Bind");
        check(rewritten.Any(instruction => Equals(instruction.operand, bind)),
            "Installed ENet host uses the scoped LAN bind wrapper");
        var menu = HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMultiplayerSubmenu), "_Ready");
        check(HarmonyLib.Harmony.GetPatchInfo(menu)?.Postfixes.Any(info => info.owner == "spirepvp.duel") == true,
            "LAN menu patch is registered independently of Steam host/join buttons");
    }
#endif

    internal static void Run(Action<bool, string> check)
    {
        foreach (var (input, expectedAddress, expectedPort) in new (string, string, ushort)[]
        {
            ("192.168.1.2", "192.168.1.2", 33771),
            (" 10.0.0.2:45000 ", "10.0.0.2", 45000),
            ("127.0.0.1:1", "127.0.0.1", 1),
            ("172.16.0.4:65535", "172.16.0.4", 65535),
            ("192.168.001.010", "192.168.1.10", 33771)
        })
        {
            check(LanAddress.TryParseEndpoint(input, out string address, out ushort port)
                && address == expectedAddress && port == expectedPort, $"LAN endpoint: {input}");
        }
        foreach (string? input in new string?[]
        {
            null, "", " ", "192.168.1", "127.1", "3232235778", "0xC0.168.1.1", "host.local",
            "192.168.1.256", "-1.2.3.4", "192. 168.1.2", "192.168..2", "192.168.1.2.3",
            "192.168.1.2:", "192.168.1.2:0", "192.168.1.2:65536", "192.168.1.2:-1",
            "192.168.1.2:+80", "192.168.1.2:1.5", "192.168.1.2:80:90", "192.168.1.2:abc",
            "http://192.168.1.2", "[::1]:33771", "::1", "0.0.0.0", "224.0.0.1", "255.255.255.255"
        })
            check(!LanAddress.TryParseEndpoint(input, out _, out _), $"Reject invalid LAN endpoint: {input ?? "null"}");
        check(LanAddress.TryParsePort(" 33771 ", out ushort defaultPort) && defaultPort == LanAddress.DefaultPort,
            "Host port accepts surrounding whitespace");
        foreach (string? input in new string?[] { null, "", "0", "65536", "-1", "+1", "1.0", "abc" })
            check(!LanAddress.TryParsePort(input, out _), $"Reject invalid host port: {input ?? "null"}");
        check(LanAddress.ClientId == 1000, "Two-player LAN uses the native client ID, distinct from host ID 1");
    }

#if LAN_ONLY
    public static void Main()
    {
        int passed = 0;
        Run((value, description) =>
        {
            if (!value) throw new InvalidOperationException(description);
            passed++;
        });
        Console.WriteLine($"PASS {passed} LAN address, port and identity checks (no game assemblies required).");
    }
#endif
}