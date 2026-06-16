using Dalamud.Configuration;
using System;

namespace ChibiChaos;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    public float ChaosScale { get; set; } = 0.55f;

    public float ExdeathScale { get; set; } = 1.0f;

    public bool DebugChat { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
