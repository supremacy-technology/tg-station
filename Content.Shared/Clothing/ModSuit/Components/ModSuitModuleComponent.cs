using Content.Shared.Clothing.ModSuit;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.ModSuit.Components;

/// <summary>
///     Marks an item as a MODsuit module that can be installed into a <see cref="ModSuitComponent"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ModSuitSystem))]
public sealed partial class ModSuitModuleComponent : Component
{
    /// <summary>
    ///     How much of the suit's complexity budget this module consumes when installed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Complexity = 1;

    /// <summary>
    ///     Whether this module can be removed again after being installed.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Removable = true;

    /// <summary>
    ///     Whether this module can be toggled on/off from the module radial menu (e.g. a flashlight).
    ///     Passive modules leave this false.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Toggleable;

    /// <summary>
    ///     Whether this module is currently switched on.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Active;

    /// <summary>
    ///     Action granted to the wearer for a toggleable module, so it can be bound to a hotkey.
    /// </summary>
    [DataField]
    public EntProtoId ToggleAction = "ActionModSuitModuleToggle";

    [DataField, AutoNetworkedField]
    public EntityUid? ToggleActionEntity;

    /// <summary>
    ///     Components granted to the wearer while this module is active (e.g. PointLight, NoSlip).
    /// </summary>
    [DataField]
    public ComponentRegistry WearerComponents = new();

    /// <summary>
    ///     Components to strip from the wearer when the module deactivates. Defaults to
    ///     <see cref="WearerComponents"/> when null.
    /// </summary>
    [DataField]
    public ComponentRegistry? RemoveComponents;

    /// <summary>
    ///     The entity the module's components were applied to, so they can be removed even if the wearer changes.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? AppliedTo;

    /// <summary>
    ///     The MODsuit this module is currently installed in, if any.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public EntityUid? InstalledSuit;
}
