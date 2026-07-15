using Robust.Shared.GameStates;

namespace Content.Shared.Clothing.ModSuit.Components;

/// <summary>
///     Marks a deployable MODsuit part (helmet, chestplate, gauntlets, boots) and links it back to the
///     <see cref="ModSuitDeployComponent"/> control unit it belongs to, so it always retracts into the
///     unit rather than being dropped or stolen.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ModSuitDeploySystem))]
public sealed partial class ModSuitPartComponent : Component
{
    /// <summary>
    ///     The control unit this part deploys from.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Master;

    /// <summary>
    ///     The inventory slot this part is deployed into.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string Slot = string.Empty;

    /// <summary>
    ///     Whether the suit has closed this part up. Drives its sealed sprite, and gates the pressure
    ///     protection it carries - an open part is just plating, as in SS13, where a part's protection
    ///     is only applied while sealed.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Sealed;
}
