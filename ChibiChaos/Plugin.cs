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

namespace ChibiChaos;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/chibichaos";
    private const float MinScale = 0.1f;
    private const float MaxScale = 2.0f;
    private const double ScanIntervalSeconds = 0.25;
    private const uint ChaosTerritoryId = 1363;
    private const uint ChaosModelCharaId = 5010;
    private const uint ChaosBaseId = 19508;
    private const string ChaosName = "Chaos";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("ChibiChaos");
    private readonly ConfigWindow configWindow;
    private readonly Dictionary<ulong, OriginalScale> originalObjectScales = [];
    private double scanTimer;

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
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        Framework.Update += OnFrameworkUpdate;
        ClientState.TerritoryChanged += OnTerritoryChanged;
    }

    public void Dispose()
    {
        RestoreScaledObjects();

        ClientState.TerritoryChanged -= OnTerritoryChanged;
        Framework.Update -= OnFrameworkUpdate;
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

    public void SetEnabled(bool enabled)
    {
        Configuration.Enabled = enabled;
        SaveConfiguration();
        if (!enabled)
        {
            RestoreScaledObjects();
        }
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

    private void OnTerritoryChanged(uint territoryType)
    {
        RestoreScaledObjects();
        scanTimer = ScanIntervalSeconds;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        scanTimer += framework.UpdateDelta.TotalSeconds;
        if (scanTimer < ScanIntervalSeconds)
        {
            return;
        }

        scanTimer = 0;
        RefreshAndApply();
    }

    private void RefreshAndApply()
    {
        if (!Configuration.Enabled || ClientState.TerritoryType != ChaosTerritoryId)
        {
            RestoreScaledObjects();
            return;
        }

        var scaledObjectIdsThisScan = new HashSet<ulong>();
        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null
                || !gameObject.IsValid()
                || gameObject is not ICharacter character
                || !IsConfiguredChaos(gameObject, character))
            {
                continue;
            }

            ApplyConfiguredScale(character, Configuration.ChaosScale);
            scaledObjectIdsThisScan.Add(gameObject.GameObjectId);
        }

        RestoreObjectsNotScaledThisScan(scaledObjectIdsThisScan);
    }

    private unsafe bool IsConfiguredChaos(IGameObject gameObject, ICharacter character)
    {
        if (gameObject is not IBattleNpc)
        {
            return false;
        }

        var native = (Character*)character.Address;
        var modelCharaIdMatches = native != null && (uint)native->ModelContainer.ModelCharaId == ChaosModelCharaId;
        var baseIdMatches = gameObject.BaseId == ChaosBaseId;
        var nameMatches = string.Equals(gameObject.Name.ToString(), ChaosName, StringComparison.OrdinalIgnoreCase);

        return modelCharaIdMatches || baseIdMatches || nameMatches;
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

            if (!originalObjectScales.ContainsKey(character.GameObjectId))
            {
                originalObjectScales[character.GameObjectId] = new OriginalScale(native->Scale, native->ModelScale);
            }

            var safeScale = ClampScale(scale);
            native->Scale = safeScale;
            native->ModelScale = safeScale;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not apply Chaos scale.");
        }
    }

    private void RestoreScaledObjects()
    {
        if (originalObjectScales.Count == 0)
        {
            return;
        }

        foreach (var gameObject in ObjectTable)
        {
            if (gameObject is not ICharacter character
                || !originalObjectScales.TryGetValue(gameObject.GameObjectId, out var originalScale))
            {
                continue;
            }

            RestoreScale(character, originalScale);
        }

        originalObjectScales.Clear();
    }

    private void RestoreObjectsNotScaledThisScan(IReadOnlySet<ulong> scaledObjectIdsThisScan)
    {
        if (originalObjectScales.Count == 0)
        {
            return;
        }

        var seenObjectIds = new HashSet<ulong>();
        var restoredObjectIds = new List<ulong>();

        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null)
            {
                continue;
            }

            seenObjectIds.Add(gameObject.GameObjectId);
            if (scaledObjectIdsThisScan.Contains(gameObject.GameObjectId)
                || gameObject is not ICharacter character
                || !originalObjectScales.TryGetValue(gameObject.GameObjectId, out var originalScale))
            {
                continue;
            }

            RestoreScale(character, originalScale);
            restoredObjectIds.Add(gameObject.GameObjectId);
        }

        foreach (var objectId in restoredObjectIds)
        {
            originalObjectScales.Remove(objectId);
        }

        foreach (var objectId in new List<ulong>(originalObjectScales.Keys))
        {
            if (!seenObjectIds.Contains(objectId))
            {
                originalObjectScales.Remove(objectId);
            }
        }
    }

    private unsafe void RestoreScale(ICharacter character, OriginalScale originalScale)
    {
        try
        {
            var native = (Character*)character.Address;
            if (native == null)
            {
                return;
            }

            native->Scale = originalScale.Scale;
            native->ModelScale = originalScale.ModelScale;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not restore Chaos scale.");
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

    private readonly record struct OriginalScale(float Scale, float ModelScale);
}
