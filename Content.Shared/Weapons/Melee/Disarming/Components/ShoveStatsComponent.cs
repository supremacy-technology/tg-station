using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.Disarming.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ShoveStatsComponent : Component
{
    [DataField] public float MoveForce = 1f;
    [DataField] public float MoveResist = 1f;
}
