# Chibi Chaos

Chibi Chaos is a Dalamud plugin that locally scales down Chaos for visibility.

The change is local only. It does not alter hitboxes, mechanics, network data, or anything seen by other players.

The plugin only runs in territory `1363`. Inside that territory, it matches Chaos if any one of these identifiers is present: ModelCharaId `5010`, BaseId `19508`, or the name `Chaos`.

Scale changes may not refresh on an already-loaded boss model. If Chaos is already present, wipe the pull or re-enter/restart the instance so the boss actor/model is recreated.

## Configuration

Open the configuration window in game:

```text
/chibichaos
```

Use the window to enable or disable the plugin and adjust `Chaos scale`.

## Dalamud Repository

Add this custom plugin repository URL in Dalamud:

```text
https://raw.githubusercontent.com/Nainaiowo/chibi-chaos/refs/heads/main/repo.json
```

Then install `Chibi Chaos` from Dalamud's plugin installer.
