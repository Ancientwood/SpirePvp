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
        var startHost = HarmonyLib.AccessTools.Method(patch, "StartHost");
        check(Equals(startHost.GetParameters()[2].DefaultValue, 2), "Existing PvP LAN hosts keep a two-player default");
        foreach (int invalidCapacity in new[] { 0, 1, 5 })
        {
            bool rejected = false;
            try { startHost.Invoke(null, [null, LanAddress.DefaultPort, invalidCapacity]); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException)
            { rejected = true; }
            check(rejected, $"Reject invalid native LAN capacity: {invalidCapacity}");
        }
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

        var nativeHost = typeof(ModEntry).Assembly.GetType("SpirePvp.NativeLanHost", throwOnError: true)!;
        check(Equals(HarmonyLib.AccessTools.Field(nativeHost, "MaxPlayers").GetRawConstantValue(), 4),
            "Native LAN rooms retain the original four-player capacity");
        var unlocked = HarmonyLib.AccessTools.Method(nativeHost, "IsUnlocked");
        check((bool)unlocked.Invoke(null, [MegaCrit.Sts2.Core.Runs.GameMode.Standard])!,
            "Standard LAN hosting requires no daily/custom unlock");
        check(!(bool)unlocked.Invoke(null, [(MegaCrit.Sts2.Core.Runs.GameMode)999])!,
            "Unsupported native LAN modes cannot be selected");
        var unlockCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(unlocked)
            .Select(instruction => instruction.operand).OfType<System.Reflection.MethodInfo>()
            .Where(method => method.IsGenericMethod).SelectMany(method => method.GetGenericArguments()).ToArray();
        check(unlockCalls.Contains(typeof(MegaCrit.Sts2.Core.Timeline.Epochs.DailyRunEpoch))
            && unlockCalls.Contains(typeof(MegaCrit.Sts2.Core.Timeline.Epochs.CustomAndSeedsEpoch)),
            "Native LAN keeps the game's daily and custom unlock checks");
        var initializeLobby = HarmonyLib.AccessTools.Method(nativeHost, "InitializeLobby");
        var lobbyCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(initializeLobby)
            .Select(instruction => instruction.operand).OfType<System.Reflection.MethodInfo>().ToArray();
        foreach (var screenType in new[]
        {
            typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen),
            typeof(MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen),
            typeof(MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen)
        })
            check(lobbyCalls.Any(method => method.DeclaringType == screenType && method.Name == "InitializeMultiplayerAsHost"),
                $"Native LAN initializes original lobby: {screenType.Name}");
        check(lobbyCalls.All(method => method.DeclaringType != typeof(PvpLobbyModifiers)
            && method.DeclaringType?.Name != "PvpUi" && method.Name != "SetModifiers" && method.Name != "SyncAscensionChange"),
            "Native LAN does not inject PvP modifiers, UI or forced ascension");
        bool invalidModeRejected = false;
        try { initializeLobby.Invoke(null, [null, null, (MegaCrit.Sts2.Core.Runs.GameMode)999]); }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is ArgumentOutOfRangeException)
        { invalidModeRejected = true; }
        check(invalidModeRejected, "Invalid LAN modes are rejected before opening a lobby");

        var lobby = (MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.Lobby.StartRunLobby));
        var modifiers = new List<MegaCrit.Sts2.Core.Models.ModifierModel>();
        var players = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer>();
        HarmonyLib.AccessTools.Field(lobby.GetType(), "_modifiers").SetValue(lobby, modifiers);
        HarmonyLib.AccessTools.Field(lobby.GetType(), "<Players>k__BackingField").SetValue(lobby, players);
        var gate = HarmonyLib.AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.TwoPlayersPatch"), "Postfix");
        foreach (int count in new[] { 2, 3, 4 })
        {
            players.Clear();
            players.AddRange(Enumerable.Range(0, count).Select(_ => default(MegaCrit.Sts2.Core.Entities.Multiplayer.StartRunLobbyPlayer)));
            modifiers.Clear();
            object?[] nativeArguments = [lobby, true];
            gate.Invoke(null, nativeArguments);
            check((bool)nativeArguments[1]!, $"Native LAN accepts {count} ready players without the PvP gate");
            modifiers.Add((PvpDuel)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(PvpDuel)));
            object?[] pvpArguments = [lobby, true];
            gate.Invoke(null, pvpArguments);
            check((bool)pvpArguments[1]! == (count == 2), $"PvP keeps the two-player rule with {count} players");
        }
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
        var nativeIds = Enumerable.Range(0, 128).Select(_ => LanAddress.CreateNativeClientId()).ToArray();
        check(nativeIds.Distinct().Count() == nativeIds.Length, "Native LAN clients generate independent identities");
        check(nativeIds.All(id => id >= 0x4000_0000_0000_0000UL && id <= long.MaxValue),
            "Native LAN IDs avoid host and reserved test-client identities");
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