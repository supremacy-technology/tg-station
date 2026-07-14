using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared.Clothing.ModSuit;

[Serializable, NetSerializable]
public enum ModSuitModuleUiKey : byte
{
    Modules,
}

/// <summary>
///     Sent from the module radial menu to toggle a single installed module on/off.
/// </summary>
[Serializable, NetSerializable]
public sealed class ModSuitModuleToggleMessage : BoundUserInterfaceMessage
{
    public NetEntity Module;

    public ModSuitModuleToggleMessage(NetEntity module)
    {
        Module = module;
    }
}

/// <summary>
///     Raised by the modules action to open the MODsuit module radial menu.
/// </summary>
public sealed partial class ModSuitModulesEvent : InstantActionEvent
{
}

/// <summary>
///     Raised on a module by its own hotkey action to toggle just that module.
/// </summary>
public sealed partial class ModSuitModuleActionEvent : InstantActionEvent
{
}
