using Robust.Shared.Serialization;

namespace Content.Shared.Clothing.ModSuit;

[Serializable, NetSerializable]
public enum ModSuitUiKey : byte
{
    Seal,
}

/// <summary>
///     Sent from the seal radial menu to toggle a single part (identified by its inventory slot).
///     When <see cref="Slot"/> is empty, every part is toggled at once.
/// </summary>
[Serializable, NetSerializable]
public sealed class ModSuitSealSlotMessage : BoundUserInterfaceMessage
{
    public string Slot;

    public ModSuitSealSlotMessage(string slot)
    {
        Slot = slot;
    }
}
