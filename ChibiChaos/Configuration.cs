using Dalamud.Configuration;
using System;

namespace ChibiChaos;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool Enabled { get; set; } = true;
    public float ChaosScale { get; set; } = 0.55f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
