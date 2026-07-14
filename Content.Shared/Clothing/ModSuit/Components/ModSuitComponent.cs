using Content.Shared.Clothing.ModSuit;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.ModSuit.Components;

/// <summary>
///     Turns a piece of outer clothing into a MODsuit ("Modular Outerwear Device") that can have
///     modules installed into it, inspired by the tgstation13 MODsuit. Each installed module takes
///     up part of a shared complexity budget.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ModSuitSystem))]
public sealed partial class ModSuitComponent : Component
{
    public const string DefaultModuleContainerId = "modsuit-modules";

    /// <summary>
    ///     Id of the container that holds installed modules.
    /// </summary>
    [DataField]
    public string ModuleContainerId = DefaultModuleContainerId;

    /// <summary>
    ///     Action that opens the module radial menu, letting the wearer toggle installed modules.
    /// </summary>
    [DataField]
    public EntProtoId ModulesAction = "ActionModSuitModules";

    [DataField, AutoNetworkedField]
    public EntityUid? ModulesActionEntity;

    /// <summary>
    ///     Action that opens the MOD interface panel (charge, complexity, modules, deploy status).
    /// </summary>
    [DataField]
    public EntProtoId PanelAction = "ActionModSuitPanel";

    [DataField, AutoNetworkedField]
    public EntityUid? PanelActionEntity;

    /// <summary>
    ///     Maximum total complexity of modules that can be installed at once. Each module has its own
    ///     complexity cost (see <see cref="ModSuitModuleComponent.Complexity"/>).
    /// </summary>
    [DataField, AutoNetworkedField]
    public int MaxComplexity = 15;

    /// <summary>
    ///     Currently used complexity, recalculated whenever a module is installed or removed.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public int UsedComplexity;

    /// <summary>
    ///     The container holding the installed modules. Populated on init.
    /// </summary>
    [ViewVariables]
    public Container? ModuleContainer;
}
