namespace Content.Shared.Weapons.Melee.Disarming;

[Flags]
public enum ShoveFlags : byte
{
    None = 0,
    CanMove = 1 << 0,             // shover.MoveForce >= target.MoveResist
    CanHitSomething = 1 << 1,     // target isn't buckled - a blocked shove can slam them into something
    Blocked = 1 << 2,             // the one-tile shove was physically stopped
    DirectionalBlocked = 1 << 3,  // stopped by a one-sided object, not a solid wall
    KnockdownBlocked = 1 << 4,    // target no-sells shove knockdown
    CanKickSide = 1 << 5,         // target was already staggered -> chains into the finisher
    CanStagger = 1 << 6,          // this shove should (re)start the staggered timer
}
