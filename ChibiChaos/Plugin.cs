using ChibiChaos.Windows;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Collections.Generic;
using GameObjectNative = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ChibiChaos;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/chibichaos";
    private const float MinScale = 0.1f;
    private const float MaxScale = 2.0f;
    private const uint ChaosTerritoryId = 1363;
    private const uint ChaosModelCharaId = 5010;
    private const uint ChaosTargetBaseId = 19508;
    private const uint ChaosModelBaseId = 19507;
    private const float ChaosBaseScale = 1.0f;
    private const uint ExdeathModelCharaId = 303;
    private const uint ExdeathBaseId = 6052;
    private const string ExdeathName = "Exdeath";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("ChibiChaos");
    private readonly ConfigWindow configWindow;
    private readonly Dictionary<nint, float> runtimeExdeathBaseScales = new();
    private bool debugRecognizedTerritory;
    private bool debugRecognizedChaos;
    private bool debugRecognizedExdeath;

    public Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        NormalizeConfiguration();

        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the Chibi Chaos configuration window.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.Draw += RefreshAndApply;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= RefreshAndApply;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        CommandManager.RemoveHandler(CommandName);

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
    }

    public void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public void SaveConfiguration()
    {
        NormalizeConfiguration();
        Configuration.Save();
    }

    public void SetScale(float scale)
    {
        Configuration.ChaosScale = ClampScale(scale);
        SaveConfiguration();
    }

    public void SetExdeathScale(float scale)
    {
        Configuration.ExdeathScale = ClampScale(scale);
        SaveConfiguration();
    }

    public void SetDebugChat(bool enabled)
    {
        Configuration.DebugChat = enabled;
        if (enabled)
        {
            debugRecognizedTerritory = false;
            debugRecognizedChaos = false;
            debugRecognizedExdeath = false;
        }

        SaveConfiguration();
    }

    private void OnCommand(string command, string args)
    {
        ToggleConfigUi();
    }

    private void RefreshAndApply()
    {
        if (ClientState.TerritoryType != ChaosTerritoryId)
        {
            runtimeExdeathBaseScales.Clear();
            UpdateDebugState(inChaosTerritory: false, chaosCount: 0, exdeathCount: 0);
            return;
        }

        var chaosCharacters = new List<ICharacter>();
        var exdeathCharacters = new List<(ICharacter Character, float BaseScale)>();
        var currentExdeathAddresses = new HashSet<nint>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null
                || !gameObject.IsValid()
                || gameObject is not ICharacter character)
            {
                continue;
            }

            if (IsConfiguredChaos(gameObject, character))
            {
                chaosCharacters.Add(character);
            }
            else if (TryGetConfiguredExdeathBaseScale(gameObject, character, out var exdeathBaseScale))
            {
                exdeathCharacters.Add((character, exdeathBaseScale));
                currentExdeathAddresses.Add(character.Address);
            }
        }

        PruneRuntimeExdeathBaseScales(currentExdeathAddresses);
        UpdateDebugState(inChaosTerritory: true, chaosCharacters.Count, exdeathCharacters.Count);
        if (chaosCharacters.Count > 0)
        {
            foreach (var character in chaosCharacters)
            {
                ApplyConfiguredScale(character, Configuration.ChaosScale, ChaosBaseScale);
            }
        }

        if (exdeathCharacters.Count > 0)
        {
            foreach (var (character, baseScale) in exdeathCharacters)
            {
                ApplyConfiguredScale(character, Configuration.ExdeathScale, baseScale);
            }
        }
    }

    private unsafe bool IsConfiguredChaos(IGameObject gameObject, ICharacter character)
    {
        var native = (Character*)character.Address;
        if (native == null)
        {
            return false;
        }

        return (uint)native->ModelContainer.ModelCharaId == ChaosModelCharaId
            && (gameObject.BaseId == ChaosTargetBaseId || gameObject.BaseId == ChaosModelBaseId);
    }

    private unsafe bool TryGetConfiguredExdeathBaseScale(IGameObject gameObject, ICharacter character, out float baseScale)
    {
        baseScale = 0.0f;
        var native = (Character*)character.Address;
        if (native == null)
        {
            return false;
        }

        var modelMatches = (uint)native->ModelContainer.ModelCharaId == ExdeathModelCharaId;
        var baseMatches = gameObject.BaseId == ExdeathBaseId;
        var nameMatches = string.Equals(gameObject.Name.TextValue, ExdeathName, StringComparison.OrdinalIgnoreCase);

        if (!nameMatches && !modelMatches && !baseMatches)
        {
            return false;
        }

        baseScale = GetExdeathBaseScale(gameObject, character, native);
        return true;
    }

    private unsafe float GetExdeathBaseScale(IGameObject gameObject, ICharacter character, Character* native)
    {
        if (TryGetKnownExdeathBaseScale(gameObject.BaseId, out var baseScale))
        {
            return baseScale;
        }

        if (runtimeExdeathBaseScales.TryGetValue(character.Address, out baseScale))
        {
            return baseScale;
        }

        var gameObjectNative = (GameObjectNative*)character.Address;
        baseScale = native->CharacterData.ModelScale;
        if (baseScale <= 0.0f)
        {
            baseScale = gameObjectNative->Scale;
        }

        if (baseScale <= 0.0f)
        {
            baseScale = 1.0f;
        }

        runtimeExdeathBaseScales[character.Address] = baseScale;
        return baseScale;
    }

    private static bool TryGetKnownExdeathBaseScale(uint baseId, out float baseScale)
    {
        // ModelChara 303 appears in multiple BNpcBase rows with different sheet scales.
        baseScale = baseId switch
        {
            1566 or 1567 or 1568 or 1569 or 8723 => 0.8f,
            239 or 242 or 245 or 8783 => 1.0f,
            6052 => 1.7f,
            2040 or 2322 or 2672 or 14430 or 14588 => 2.0f,
            _ => 0.0f,
        };

        return baseScale > 0.0f;
    }

    private void PruneRuntimeExdeathBaseScales(HashSet<nint> currentExdeathAddresses)
    {
        if (runtimeExdeathBaseScales.Count == 0)
        {
            return;
        }

        var staleAddresses = new List<nint>();
        foreach (var address in runtimeExdeathBaseScales.Keys)
        {
            if (!currentExdeathAddresses.Contains(address))
            {
                staleAddresses.Add(address);
            }
        }

        foreach (var address in staleAddresses)
        {
            runtimeExdeathBaseScales.Remove(address);
        }
    }

    private unsafe void ApplyConfiguredScale(ICharacter character, float scale, float baseScale)
    {
        try
        {
            var native = (Character*)character.Address;
            if (native == null)
            {
                return;
            }

            var safeScale = baseScale * ClampScale(scale);
            var gameObjectNative = (GameObjectNative*)character.Address;
            gameObjectNative->Scale = safeScale;
            native->CharacterData.ModelScale = safeScale;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply character scale.");
        }
    }

    private void UpdateDebugState(bool inChaosTerritory, int chaosCount, int exdeathCount)
    {
        var chaosRecognized = inChaosTerritory && chaosCount > 0;
        var exdeathRecognized = inChaosTerritory && exdeathCount > 0;
        if (!Configuration.DebugChat)
        {
            debugRecognizedTerritory = inChaosTerritory;
            debugRecognizedChaos = chaosRecognized;
            debugRecognizedExdeath = exdeathRecognized;
            return;
        }

        if (inChaosTerritory && !debugRecognizedTerritory)
        {
            PrintDebug("Recognized Dancing Mad (Ultimate) / Sigmascape V4.0.");
        }
        else if (!inChaosTerritory && debugRecognizedTerritory)
        {
            PrintDebug("Left Dancing Mad (Ultimate) / Sigmascape V4.0.");
        }

        if (chaosRecognized && !debugRecognizedChaos)
        {
            PrintDebug($"Found Chaos ({chaosCount} matching actor(s)).");
        }
        else if (!chaosRecognized && debugRecognizedChaos)
        {
            PrintDebug("Chaos no longer recognized.");
        }

        if (exdeathRecognized && !debugRecognizedExdeath)
        {
            PrintDebug($"Found Exdeath ({exdeathCount} matching actor(s)).");
        }
        else if (!exdeathRecognized && debugRecognizedExdeath)
        {
            PrintDebug("Exdeath no longer recognized.");
        }

        debugRecognizedTerritory = inChaosTerritory;
        debugRecognizedChaos = chaosRecognized;
        debugRecognizedExdeath = exdeathRecognized;
    }

    private static void PrintDebug(string message)
    {
        ChatGui.Print($"[Chibi Chaos] {message}");
    }

    private void NormalizeConfiguration()
    {
        Configuration.ChaosScale = ClampScale(Configuration.ChaosScale);
        Configuration.ExdeathScale = ClampScale(Configuration.ExdeathScale);
    }

    private static float ClampScale(float scale)
    {
        return Math.Clamp(scale, MinScale, MaxScale);
    }
}
