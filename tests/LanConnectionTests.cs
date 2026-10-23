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
        foreach (int invalidCapacity in new[] { 0, 1, 3, 4, 5 })
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
            "LAN replacement is registered on the original multiplayer menu");
        var joinOpened = HarmonyLib.AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NJoinFriendScreen), "OnSubmenuOpened");
        check(HarmonyLib.Harmony.GetPatchInfo(joinOpened)?.Prefixes.Any(info => info.owner == "spirepvp.duel") == true,
            "LAN join page replaces automatic Steam lookup and localhost joining");
        var enetHost = (MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetHost)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Multiplayer.Transport.ENet.ENetHost));
        check(enetHost.NetId == LanAddress.HostId, "Native ENet host identity is always A1, not a display-only alias");
        var names = HarmonyLib.AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.LanPlayerNamesPatch"), "Postfix");
        var saveRouting = typeof(ModEntry).Assembly.GetType("SpirePvp.LanRunSaves");
        HarmonyLib.AccessTools.Method(saveRouting, "Activate").Invoke(null, [true]);
        foreach (var (id, expected) in new[] { (LanAddress.HostId, "A1"), (LanAddress.NativeClientId, "A2"), (99UL, "existing") })
        {
            object?[] arguments = [id, "existing"];
            names.Invoke(null, arguments);
            check((string)arguments[1]! == expected, $"LAN identity display: {id} -> {expected}");
        }
        HarmonyLib.AccessTools.Method(saveRouting, "ClearActive").Invoke(null, null);
        object?[] outsideLan = [LanAddress.HostId, "original name"];
        names.Invoke(null, outsideLan);
        check((string)outsideLan[1]! == "original name", "A1/A2 labels do not change names outside native LAN");
        var service = (MegaCrit.Sts2.Core.Multiplayer.NetHostGameService)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Multiplayer.NetHostGameService));
        var capacity = HarmonyLib.AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.LanLobbyCapacityPatch"), "Prefix");
        object?[] capacityArguments = [service, 4];
        capacity.Invoke(null, capacityArguments);
        check((int)capacityArguments[1]! == 4, "Other network services keep their original lobby capacity");
        HarmonyLib.AccessTools.Method(patch, "Register").Invoke(null, [service]);
        capacity.Invoke(null, capacityArguments);
        check((int)capacityArguments[1]! == 2, "Owned LAN lobbies, including daily runs, are limited to two players");

        var nativeHost = typeof(ModEntry).Assembly.GetType("SpirePvp.NativeLanHost", throwOnError: true)!;
        check(Equals(HarmonyLib.AccessTools.Field(nativeHost, "MaxPlayers").GetRawConstantValue(), 2),
            "Native LAN rooms have a fixed two-player capacity");
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
        var loadCalls = HarmonyLib.PatchProcessor.GetOriginalInstructions(HarmonyLib.AccessTools.Method(nativeHost, "InitializeLoadLobby"))
            .Select(instruction => instruction.operand).OfType<System.Reflection.MethodInfo>().ToArray();
        foreach (string screenName in new[] { "NMultiplayerLoadGameScreen", "NDailyRunLoadScreen", "NCustomRunLoadScreen" })
            check(loadCalls.Any(method => method.Name == "InitializeAsHost" && method.DeclaringType?.Name == screenName),
                $"LAN resume uses the original synchronized load lobby: {screenName}");

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

    internal static void RunSaveChecks(Action<bool, string> check)
    {
        var saves = typeof(ModEntry).Assembly.GetType("SpirePvp.LanRunSaves", throwOnError: true)!;
        string nativeFile = (string)HarmonyLib.AccessTools.Field(saves, "NativeFile").GetRawConstantValue()!;
        string pvpFile = (string)HarmonyLib.AccessTools.Field(saves, "PvpFile").GetRawConstantValue()!;
        var compatible = HarmonyLib.AccessTools.Method(saves, "IsCompatible");
        bool Matches(MegaCrit.Sts2.Core.Saves.SerializableRun run, bool native) => (bool)compatible.Invoke(null, [run, native])!;
        var nativeRun = new MegaCrit.Sts2.Core.Saves.SerializableRun
        {
            PlatformType = MegaCrit.Sts2.Core.Platform.PlatformType.None,
            Players = [new() { NetId = LanAddress.HostId }, new() { NetId = LanAddress.NativeClientId }]
        };
        check(Matches(nativeRun, true) && !Matches(nativeRun, false), "A1/A2 native save is distinct from a PvP save");
        nativeRun.Players[1].NetId = 999999999;
        check(!Matches(nativeRun, true), "Old random-client saves are not silently rewritten or accepted");
        nativeRun.Players[1].NetId = LanAddress.HostId;
        check(!Matches(nativeRun, true), "Duplicate A1 identities cannot pass save validation");
        nativeRun.Players[1].NetId = LanAddress.NativeClientId;
        nativeRun.Players.Add(new() { NetId = 3 });
        check(!Matches(nativeRun, true), "Three-player saves cannot resume in a two-player room");
        nativeRun.Players.RemoveAt(2);
        var pvpRun = new MegaCrit.Sts2.Core.Saves.SerializableRun
        {
            PlatformType = MegaCrit.Sts2.Core.Platform.PlatformType.None,
            Players = [new() { NetId = LanAddress.HostId }, new() { NetId = LanAddress.ClientId }],
            Modifiers = PvpLobbyModifiers.Create(false).Select(modifier => modifier.ToSerializable()).ToList()
        };
        check(Matches(pvpRun, false) && !Matches(pvpRun, true), "PvP modifiers and identities select only the PvP save slot");
        var expectedClient = HarmonyLib.AccessTools.Method(typeof(ModEntry).Assembly.GetType("SpirePvp.LanLobbyIdentityPatch"), "ExpectedClientId");
        check((ulong)expectedClient.Invoke(null, [Array.Empty<MegaCrit.Sts2.Core.Models.ModifierModel>()])! == LanAddress.NativeClientId,
            "Native hosts accept only the fixed A2 identity");
        check((ulong)expectedClient.Invoke(null, [PvpLobbyModifiers.Create(false)])! == LanAddress.ClientId,
            "PvP hosts reject clients using the native LAN identity");

        var store = System.Reflection.DispatchProxy.Create<MegaCrit.Sts2.Core.Saves.ISaveStore, LanSaveStoreFixture>();
        var fixture = (LanSaveStoreFixture)(object)store;
        var manager = (MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager));
        HarmonyLib.AccessTools.Field(manager.GetType(), "_saveStore").SetValue(manager, store);
        var shim = new HarmonyLib.Harmony("spirepvp.tests.lan-save-paths");
        var prefix = new HarmonyLib.HarmonyMethod(typeof(LanSaveStoreFixture), nameof(LanSaveStoreFixture.SavePathPrefix));
        var multiplayerPath = HarmonyLib.AccessTools.PropertyGetter(manager.GetType(), "CurrentMultiplayerRunSavePath");
        var singleplayerPath = HarmonyLib.AccessTools.PropertyGetter(manager.GetType(), "CurrentRunSavePath");
        shim.Patch(multiplayerPath, prefix: prefix);
        shim.Patch(singleplayerPath, prefix: prefix);
        try
        {
            fixture.Files["profiles/1/saves/current_run_mp.save"] = "existing Steam save";
            fixture.Files["profiles/1/saves/current_run_mp.save.backup"] = "existing Steam backup";
            manager.SaveRun(nativeRun, true).GetAwaiter().GetResult();
            string nativePath = "profiles/1/saves/" + nativeFile;
            string pvpPath = "profiles/1/saves/" + pvpFile;
            check(fixture.Files.ContainsKey(nativePath), "Actual native SaveRun writes the isolated A1/A2 slot");
            check(fixture.Files["profiles/1/saves/current_run_mp.save"] == "existing Steam save"
                && fixture.Files["profiles/1/saves/current_run_mp.save.backup"] == "existing Steam backup",
                "LAN saving leaves existing Steam save and backup untouched");
            var restored = System.Text.Json.JsonSerializer.Deserialize(fixture.Files[nativePath],
                MegaCrit.Sts2.Core.Saves.JsonSerializationUtility.GetTypeInfo<MegaCrit.Sts2.Core.Saves.SerializableRun>())!;
            check(Matches(restored, true) && restored.Players.Select(player => player.NetId).SequenceEqual(new[] { 1UL, 2UL }),
                "Native serialization round trip retains fixed A1/A2 identities for resume");
            manager.SaveRun(pvpRun, true).GetAwaiter().GetResult();
            check(fixture.Files.ContainsKey(pvpPath) && fixture.Files.ContainsKey(nativePath), "PvP and native LAN saves coexist");
            manager.SaveRun(nativeRun, false).GetAwaiter().GetResult();
            check(fixture.Files.ContainsKey("profiles/1/saves/current_run.save"), "Singleplayer retains its original save path");
            check((string)multiplayerPath.Invoke(manager, null)! == "profiles/1/saves/current_run_mp.save",
                "LAN save scope does not leak into later native save operations");
            HarmonyLib.AccessTools.Method(saves, "Activate").Invoke(null, [true]);
            nativeRun.Players[1].NetId = 99;
            bool invalidSaveRejected = false;
            try { manager.SaveRun(nativeRun, true).GetAwaiter().GetResult(); }
            catch (InvalidDataException) { invalidSaveRejected = true; }
            finally
            {
                nativeRun.Players[1].NetId = LanAddress.NativeClientId;
                HarmonyLib.AccessTools.Method(saves, "ClearActive").Invoke(null, null);
            }
            check(invalidSaveRejected && fixture.Files["profiles/1/saves/current_run_mp.save"] == "existing Steam save",
                "Incompatible active LAN snapshots cannot fall back to overwriting the Steam slot");
            fixture.FailWrites = true;
            bool failed = false;
            try { manager.SaveRun(nativeRun, true).GetAwaiter().GetResult(); }
            catch (IOException) { failed = true; }
            check(failed && HarmonyLib.AccessTools.PropertyGetter(saves, "CurrentFile").Invoke(null, null) == null,
                "Failed asynchronous writes restore the previous save routing scope");
        }
        finally
        {
            shim.Unpatch(multiplayerPath, HarmonyLib.HarmonyPatchType.All, shim.Id);
            shim.Unpatch(singleplayerPath, HarmonyLib.HarmonyPatchType.All, shim.Id);
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
        check(LanAddress.HostId == 1 && LanAddress.NativeClientId == 2, "Native LAN uses fixed A1/A2 numeric identities");
        check(LanAddress.HostId != LanAddress.NativeClientId && LanAddress.NativeClientId != LanAddress.ClientId,
            "Native host, native client and legacy PvP client identities remain distinct");
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

#if !LAN_ONLY
public class LanSaveStoreFixture : System.Reflection.DispatchProxy
{
    public Dictionary<string, string> Files { get; } = new();
    public bool FailWrites { get; set; }

    public static bool SavePathPrefix(System.Reflection.MethodBase __originalMethod, ref string __result)
    {
        __result = __originalMethod.Name.Contains("Multiplayer")
            ? "profiles/1/saves/current_run_mp.save" : "profiles/1/saves/current_run.save";
        return false;
    }

    protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args)
    {
        string path = (string)args![0]!;
        switch (targetMethod!.Name)
        {
            case "WriteFileAsync":
                return WriteAsync(path, args[1]!);
            case "WriteFile":
                Write(path, args[1]!);
                return null;
            case "ReadFile":
                return Files.GetValueOrDefault(path);
            case "FileExists":
                return Files.ContainsKey(path);
            case "DeleteFile":
                Files.Remove(path);
                return null;
            default:
                throw new NotSupportedException(targetMethod.Name);
        }
    }

    private async Task WriteAsync(string path, object content)
    {
        await Task.Yield();
        Write(path, content);
    }

    private void Write(string path, object content)
    {
        if (FailWrites) throw new IOException("Simulated LAN save failure");
        Files[path] = content is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes) : (string)content;
    }
}
#endif