# Station Item Return

Returns stored items from supported Valheim stations one at a time without destroying the station.

The support rule follows the station's native destruction behavior: a stored item type is exposed only when Valheim can determine and return that same item when the station is destroyed. Existing vanilla actions remain responsible for already-accessible finished products.

## Controls

Hold **Alt Place** (`Shift` by default) and press **Use** while looking at the relevant station interaction point.

A return hint is shown only when all of the following are true:

- the corresponding server-controlled feature is enabled;
- the local player has access to the station piece;
- the station currently contains an item that can be returned;
- the station's return-on-destruction rules identify the exact item type;
- the configured cheated-queue policy allows the operation.

If any condition fails, the mod leaves the original interaction unchanged.

## Supported returns

| Station type | Interaction point | Returned item | Default |
| --- | --- | --- | --- |
| Cooking station / oven | Food input | Last uncooked input item | Enabled |
| Cooking station / oven | Fuel input | One remaining unit of the station's fixed fuel item | Enabled |
| Smelter / kiln / refinery | Material input | Newest queued input item | Enabled |
| Smelter / kiln / refinery | Fuel input | One remaining unit of the station's fixed fuel item | Enabled |
| Fermenter | Station body | Original ingredient while fermenting | Enabled |
| Turret | Station body | One loaded ammunition item when `m_returnAmmoOnDestroy` is enabled | Disabled |

Completed cooking items, smelter output, and ready fermenter products already have native extraction actions and are not replaced by this mod.

### Fuel precision

Cooking stations and smelters store fuel as a floating-point amount even though the UI presents it as whole fuel units. A station starts consuming the final unit immediately, so its stored value can be between `0` and `1` while the UI still shows one remaining fuel item.

The mod treats every positive fuel amount as one returnable remaining unit:

- return is available whenever stored fuel is greater than `0`;
- one interaction subtracts `1.0`, clamped to `0`;
- the final partially consumed fuel unit can be returned as one item.

If another mod permits alternative fuels but the station still stores only a numeric fuel amount, the return produces the station's fixed `m_fuelItem`. The mod does not guess which alternative item was originally consumed.

Fireplaces are not supported because Valheim does not return their stored fuel when the piece is destroyed. Shield generators are not supported because they accept multiple fuel item types but store only an aggregate fuel amount, so the original item type cannot be reconstructed reliably.

## Smelter input order

Smelter input is always returned from the tail of the queue, newest first. The final remaining queue entry can also be returned. When the queue becomes empty, the smelter bake timer and queue-level cheated marker are reset.

## Smelter cheated-queue policy

Valheim stores one `ZDOVars.s_cheatedQueued` flag for the complete smelter queue rather than one flag per queued item.

`Smelters / Allow marked queue return` controls the unavoidable ambiguity:

- `false` (default): returning input is blocked while the queue is marked as cheated;
- `true`: returning input is allowed and the returned item is marked as cheated. The remaining queue keeps its current marker. The marker is cleared only when the queue becomes empty.

## Cooking skill credit

Valheim awards `0.4` Cooking skill only when the normal default-item interaction inserts an item. Selecting an item through the contextual radial menu does not award that credit.

The mod records the author and credit state for each cooking slot. Returning an item reverses `0.4` only for the original player and only when that exact slot received the vanilla credit.

## Turret ammunition

Turret return is disabled by default. It is offered only for turret prefabs whose `m_returnAmmoOnDestroy` setting is enabled.

Valheim stores the ammunition type and count but not cheated provenance. Consequently, returned ammunition is always created without the cheated marker. This limitation is stated in the configuration description.

## Configuration

All behavior-changing settings are always controlled by the server:

```text
General / Enabled
Cooking stations / Return uncooked items
Cooking stations / Return fuel
Smelters / Return input items
Smelters / Return fuel
Smelters / Allow marked queue return
Fermenters / Return ingredient
Turrets / Return ammunition
```

`General / Enable logging` is client-local.

## Compatibility

The hover hint is inserted into the unlocalized hover string immediately after the first `$KEY_Use` action. A final localized postfix also handles formatters that replace the vanilla hover result, including My Little UI.

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)

## Discord
[Join server](https://discord.gg/e3UtQB8GFK)
