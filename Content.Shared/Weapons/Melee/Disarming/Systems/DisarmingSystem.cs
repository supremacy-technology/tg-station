using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Movement.TileMovement.Systems;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Weapons.Melee.Disarming.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Melee.Disarming.Systems;

public sealed class DisarmingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly TileMovementSystem _tileMove = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    // Used by IsStanding/IsBuckled below - see the remarks on those methods for the assumptions made.
    [Dependency] private readonly EntityQuery<StandingStateComponent> _standingQuery = default!;
    [Dependency] private readonly EntityQuery<BuckleComponent> _buckleQuery = default!;

    private static readonly TimeSpan StaggerLength = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StaggerMax = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KnockdownDaze = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KickChainParalyze = TimeSpan.FromSeconds(2);

    public bool CanDisarm(EntityUid disarmer, EntityUid target)
    {
        if (disarmer == target)
            return false;

        if (!IsStanding(disarmer)) // TODO: your standing/knockdown check
            return false;

        if (_tileMove.GetTile(disarmer) == _tileMove.GetTile(target)) // TODO: your tile accessor
            return false;

        return true;
    }

    public void TryDisarm(EntityUid disarmer, EntityUid target, EntityUid? weapon)
    {
        if (!CanDisarm(disarmer, target))
            return;

        var flags = GetShoveFlags(disarmer, target);

        var hitEv = new DisarmHitEvent(disarmer, weapon);
        RaiseLocalEvent(target, ref hitEv);

        var fromMap = _transform.ToMapCoordinates(Transform(disarmer).Coordinates);
        var toMap = _transform.ToMapCoordinates(Transform(target).Coordinates);
        var shoveDir = (toMap.Position - fromMap.Position).ToWorldAngle().GetDir(); // same helper your client uses for arc casts

        var targetCoords = Transform(target).Coordinates;
        var destCoords = _tileMove.GetAdjacentTile(targetCoords, shoveDir); // TODO: your actual method name

        if ((flags & ShoveFlags.CanMove) != 0)
        {
            var preShove = new DisarmPreShoveEvent(disarmer, target);
            foreach (var occupant in _lookup.GetEntitiesIntersecting(destCoords, LookupFlags.Uncontained))
                RaiseLocalEvent(occupant, ref preShove);

            if (preShove.Solid)
            {
                flags |= ShoveFlags.Blocked;
            }
            else
            {
                var before = targetCoords;
                _tileMove.TryMoveTile(target, shoveDir); // TODO: your actual move-attempt method
                if (Transform(target).Coordinates.Equals(before))
                    flags |= ShoveFlags.Blocked;
            }
        }

        if ((flags & ShoveFlags.Blocked) == 0)
        {
            // BreakGrab(target); // only if you have grab-level escalation, downgrade it here
        }

        if ((flags & ShoveFlags.Blocked) != 0 && IsCardinal(shoveDir)
            && IsDirectionallyBlocked(targetCoords, destCoords, shoveDir))
        {
            flags |= ShoveFlags.DirectionalBlocked;
        }

        if ((flags & ShoveFlags.CanHitSomething) != 0)
        {
            if ((flags & ShoveFlags.DirectionalBlocked) == 0)
            {
                var collide = new DisarmCollideEvent(disarmer, target, flags);
                foreach (var occupant in _lookup.GetEntitiesIntersecting(destCoords, LookupFlags.Uncontained))
                {
                    RaiseLocalEvent(occupant, ref collide);
                    if (collide.Handled)
                        break;
                }
                if (collide.Handled)
                    return; // something else took over - e.g. shoved into another mob
            }

            if ((flags & ShoveFlags.Blocked) != 0 && (flags & (ShoveFlags.KnockdownBlocked | ShoveFlags.CanKickSide)) == 0)
            {
                _stun.TryKnockdown(target, KnockdownDaze, refresh: true); // TODO: your stun system
                _popup.PopupPredicted(Loc.GetString("disarm-shove-knockdown"), target, target);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Low,
                    $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target} into something solid, knocking them down");
                return;
            }
        }

        if ((flags & ShoveFlags.CanKickSide) != 0)
        {
            // _stun.TryParalyze(target, KickChainParalyze, refresh: true); // TODO: your stun system
            _popup.PopupPredicted(Loc.GetString("disarm-kick-chain"), target, target);
            _adminLogger.Add(LogType.MeleeHit, LogImpact.Medium,
                $"{ToPrettyString(disarmer):disarmer} kicked {ToPrettyString(target):target} onto their side");
            return;
        }

        _popup.PopupPredicted(Loc.GetString("disarm-shove"), target, disarmer);

        if (_hands.TryGetActiveItem(target, out var heldItem))
        {
            var staggeredNow = TryComp<StaggeredComponent>(target, out var s) && s.StaggeredUntil > _timing.CurTime;
            var droppable = _tag.HasTag(heldItem.Value, "DisarmDroppable");
            var lyingDown = !IsStanding(target); // TODO: same standing check as above

            if ((staggeredNow && droppable) || lyingDown)
            {
                _hands.TryDrop(target, heldItem.Value);
                _popup.PopupPredicted(Loc.GetString("disarm-item-dropped", ("item", heldItem.Value)), target, disarmer);
            }
        }

        if ((flags & ShoveFlags.CanStagger) != 0)
        {
            var stagger = EnsureComp<StaggeredComponent>(target);
            var floor = _timing.CurTime + StaggerLength;
            var cap = _timing.CurTime + StaggerMax;
            var newUntil = stagger.StaggeredUntil > floor ? stagger.StaggeredUntil : floor;
            stagger.StaggeredUntil = newUntil > cap ? cap : newUntil;
            Dirty(target, stagger);
        }

        _adminLogger.Add(LogType.MeleeHit, LogImpact.Low, $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target}");
    }

    private ShoveFlags GetShoveFlags(EntityUid disarmer, EntityUid target)
    {
        var flags = ShoveFlags.None;

        var force = CompOrNull<ShoveStatsComponent>(disarmer)?.MoveForce ?? 1f;
        var resist = CompOrNull<ShoveStatsComponent>(target)?.MoveResist ?? 1f;

        if (force >= resist)
        {
            flags |= ShoveFlags.CanMove;
            if (!IsBuckled(target)) // TODO: your buckle/seat check, if any
                flags |= ShoveFlags.CanHitSomething;
        }

        // TODO: your equivalent of TRAIT_BRAWLING_KNOCKDOWN_BLOCKED
        // if (HasComp<KnockdownImmuneComponent>(target)) flags |= ShoveFlags.KnockdownBlocked;

        // Your pasted get_shove_flags() never sets CanKickSide/CanStagger - in tgstation that comes
        // from a /mob/living/carbon override keyed on the target's existing stagger. Reconstructed
        // here: already staggered -> this shove chains into the kick; otherwise it starts the clock.
        if (TryComp<StaggeredComponent>(target, out var staggered) && staggered.StaggeredUntil > _timing.CurTime)
            flags |= ShoveFlags.CanKickSide;
        else
            flags |= ShoveFlags.CanStagger;

        return flags;
    }

    /// <summary>
    /// Whether the entity is currently on its feet.
    ///
    /// ASSUMPTION: wired against Content.Shared.Standing.StandingStateComponent (bool "Standing" field),
    /// which is the component upstream SS14 uses for knockdown/crawling. If your fork tracks this
    /// differently (a status-effect key, a custom component, etc), swap the body of this method for
    /// that check - everything above just calls IsStanding(uid) and doesn't care how it's implemented.
    /// Entities with no StandingStateComponent at all (e.g. non-mobs) are treated as standing.
    /// </summary>
    private bool IsStanding(EntityUid uid)
    {
        return !_standingQuery.TryComp(uid, out var standing) || standing.Standing;
    }

    /// <summary>
    /// Whether the entity is currently buckled/seated (and so shouldn't be shoved into anything).
    ///
    /// ASSUMPTION: wired against Content.Shared.Buckle.Components.BuckleComponent (bool "Buckled" field).
    /// Swap this out if your fork doesn't have buckling, or tracks it differently - the caller only
    /// cares about the bool.
    /// </summary>
    private bool IsBuckled(EntityUid uid)
    {
        return _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled;
    }

    private bool IsDirectionallyBlocked(EntityCoordinates fromTile, EntityCoordinates toTile, Direction shoveDir)
    {
        foreach (var ent in _lookup.GetEntitiesIntersecting(fromTile, LookupFlags.Uncontained))
            if (TryComp<DirectionalBlockerComponent>(ent, out var b) && b.BlocksFrom == shoveDir)
                return true;

        if (!fromTile.Equals(toTile))
            foreach (var ent in _lookup.GetEntitiesIntersecting(toTile, LookupFlags.Uncontained))
                if (TryComp<DirectionalBlockerComponent>(ent, out var b) && b.BlocksFrom == shoveDir.GetOpposite())
                    return true;

        return false;
    }

    private static bool IsCardinal(Direction dir) =>
        dir is Direction.North or Direction.South or Direction.East or Direction.West;
}
