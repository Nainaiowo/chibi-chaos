using Dalamud.Configuration;
using System;

namespace ChibiChaos;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public float ChaosScale { get; set; } = 0.55f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
