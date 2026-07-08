namespace Content.Shared.Weapons.Melee.Disarming;

// mirrors COMSIG_LIVING_DISARM_HIT
[ByRefEvent]
public record struct DisarmHitEvent(EntityUid Disarmer, EntityUid? Weapon);

// mirrors COMSIG_LIVING_DISARM_PRESHOVE - raised on whatever occupies the destination tile
[ByRefEvent]
public record struct DisarmPreShoveEvent(EntityUid Disarmer, EntityUid Target)
{
    public bool Solid;
}

// mirrors COMSIG_LIVING_DISARM_COLLIDE
[ByRefEvent]
public record struct DisarmCollideEvent(EntityUid Disarmer, EntityUid Target, ShoveFlags Flags)
{
    public bool Handled;
}
