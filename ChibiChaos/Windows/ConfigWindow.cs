using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace ChibiChaos.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("Chibi Chaos###ChibiChaosConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        Size = new Vector2(420, 160);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var scale = configuration.ChaosScale;
        if (ImGui.SliderFloat("Chaos scale", ref scale, 0.1f, 2.0f, "%.2f"))
        {
            plugin.SetScale(scale);
        }

        var exdeathScale = configuration.ExdeathScale;
        if (ImGui.SliderFloat("Exdeath scale", ref exdeathScale, 0.1f, 2.0f, "%.2f"))
        {
            plugin.SetExdeathScale(exdeathScale);
        }

        var debugChat = configuration.DebugChat;
        if (ImGui.Checkbox("Debug chat", ref debugChat))
        {
            plugin.SetDebugChat(debugChat);
        }

        ImGui.TextWrapped($"Chaos scale percent: {MathF.Round(configuration.ChaosScale * 100.0f)}%");
        ImGui.TextWrapped($"Exdeath scale percent: {MathF.Round(configuration.ExdeathScale * 100.0f)}%");
        ImGui.TextWrapped("Scale changes may require a wipe or instance re-entry before already-loaded boss models refresh.");
    }
}
