using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared.Clothing.ModSuit;

[Serializable, NetSerializable]
public enum ModSuitPanelUiKey : byte
{
    Key,
}

/// <summary>
///     Raised by the panel action to open the MOD interface panel.
/// </summary>
public sealed partial class ModSuitPanelEvent : InstantActionEvent
{
}
