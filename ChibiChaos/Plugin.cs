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
    private const uint ExdeathModelCharaId = 303;
    private const uint ExdeathBaseId = 6052;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("ChibiChaos");
    private readonly ConfigWindow configWindow;
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
            UpdateDebugState(inChaosTerritory: false, chaosCount: 0, exdeathCount: 0);
            return;
        }

        var chaosCharacters = new List<ICharacter>();
        var exdeathCharacters = new List<ICharacter>();
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
            else if (IsConfiguredExdeath(gameObject, character))
            {
                exdeathCharacters.Add(character);
            }
        }

        UpdateDebugState(inChaosTerritory: true, chaosCharacters.Count, exdeathCharacters.Count);
        if (chaosCharacters.Count > 0)
        {
            foreach (var character in chaosCharacters)
            {
                ApplyConfiguredScale(character, Configuration.ChaosScale);
            }
        }

        if (exdeathCharacters.Count > 0)
        {
            foreach (var character in exdeathCharacters)
            {
                ApplyConfiguredScale(character, Configuration.ExdeathScale);
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

    private unsafe bool IsConfiguredExdeath(IGameObject gameObject, ICharacter character)
    {
        var native = (Character*)character.Address;
        if (native == null)
        {
            return false;
        }

        return (uint)native->ModelContainer.ModelCharaId == ExdeathModelCharaId
            && gameObject.BaseId == ExdeathBaseId;
    }

    private unsafe void ApplyConfiguredScale(ICharacter character, float scale)
    {
        try
        {
            var native = (Character*)character.Address;
            if (native == null)
            {
                return;
            }

            var safeScale = ClampScale(scale);
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
