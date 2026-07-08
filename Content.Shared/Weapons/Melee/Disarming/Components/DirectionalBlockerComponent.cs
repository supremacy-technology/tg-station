namespace Content.Shared.Weapons.Melee.Disarming.Components;

// SS14 has no ON_BORDER_1 concept, so this is the closest equivalent for windoors/grilles that
// only block from one side of their tile.
[RegisterComponent]
public sealed partial class DirectionalBlockerComponent : Component
{
    [DataField] public Direction BlocksFrom = Direction.South;
}
