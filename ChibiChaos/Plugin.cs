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

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("ChibiChaos");
    private readonly ConfigWindow configWindow;

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

    private void OnCommand(string command, string args)
    {
        ToggleConfigUi();
    }

    private void RefreshAndApply()
    {
        if (ClientState.TerritoryType != ChaosTerritoryId)
        {
            return;
        }

        var chaosCharacters = new List<ICharacter>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null
                || !gameObject.IsValid()
                || gameObject is not ICharacter character
                || !IsConfiguredChaos(gameObject, character))
            {
                continue;
            }

            chaosCharacters.Add(character);
        }

        if (chaosCharacters.Count > 0)
        {
            foreach (var character in chaosCharacters)
            {
                ApplyConfiguredScale(character, Configuration.ChaosScale);
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
            Log.Warning(ex, "Could not apply Chaos scale.");
        }
    }

    private void NormalizeConfiguration()
    {
        Configuration.ChaosScale = ClampScale(Configuration.ChaosScale);
    }

    private static float ClampScale(float scale)
    {
        return Math.Clamp(scale, MinScale, MaxScale);
    }
}
