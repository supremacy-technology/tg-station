using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Disarming.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Melee.Disarming.Systems;

/// <summary>
/// SS13-style shove/disarm ("disable"), ported to work with normal SS14 continuous movement.
///
/// The target is shoved one tile in the direction the disarmer is facing them (a physics throw, not a
/// tile-lock step). If the destination tile is solid - a wall or another mob - the shove is "blocked"
/// and the target is knocked down instead. Repeated shoves stagger, then chain into a knockdown/kick.
///
/// This system is deliberately self-contained: it depends only on standard movement/physics
/// (<see cref="ThrowingSystem"/>, <see cref="TurfSystem"/>) so it works whether or not any tile-locked
/// movement prototype is present.
/// </summary>
public sealed class DisarmingSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;

    [Dependency] private readonly EntityQuery<StandingStateComponent> _standingQuery = default!;
    [Dependency] private readonly EntityQuery<BuckleComponent> _buckleQuery = default!;
    [Dependency] private readonly EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private readonly EntityQuery<MapGridComponent> _gridQuery = default!;

    // Walls + mobs; matches what a mob itself collides against, so "blocked" means "shoved into
    // something they couldn't have walked through".
    private const CollisionGroup ShoveMask = CollisionGroup.MobMask;
    private const float ShoveThrowSpeed = 5f;
    private const LookupFlags TileLookup = LookupFlags.Dynamic | LookupFlags.Static;

    private static readonly ProtoId<TagPrototype> DisarmDroppableTag = "DisarmDroppable";

    private static readonly TimeSpan StaggerLength = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StaggerMax = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KnockdownDaze = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KickChainParalyze = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Universal conditions for a shove: the disarmer is on their feet, isn't shoving themselves, and
    /// isn't standing on the exact same tile as the target (there'd be no direction to shove them).
    /// Mirrors SS13 can_disarm().
    /// </summary>
    public bool CanDisarm(EntityUid disarmer, EntityUid target)
    {
        if (disarmer == target)
            return false;

        if (!IsStanding(disarmer))
            return false;

        if (GetTile(disarmer) is { } a && GetTile(target) is { } b && a == b)
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

        // Shove the target straight away from the disarmer (SS13 get_dir(user, target)). The delta is a
        // world-space vector, but the tile step below and the tile the target sits on are in the grid's
        // frame - so rotate the delta into the grid frame before turning it into a Direction. Skipping
        // this made the push come out 90 degrees off whenever the grid/eye was rotated relative to the
        // world (which is what "he pushes right when I'm above him" was).
        var targetXform = Transform(target);
        var delta = _transform.GetMapCoordinates(target).Position - _transform.GetMapCoordinates(disarmer).Position;
        var gridRot = targetXform.GridUid is { } shoveGrid ? _transform.GetWorldRotation(shoveGrid) : Angle.Zero;
        var shoveDir = (-gridRot).RotateVec(delta).GetDir();

        var targetCoords = targetXform.Coordinates;

        // The tile we'd be shoving them onto (may be null if they're off-grid / at a grid edge).
        var destTile = GetAdjacentTile(target, shoveDir, out var destCoords);
        destCoords ??= targetCoords;

        if ((flags & ShoveFlags.CanMove) != 0)
        {
            var preShove = new DisarmPreShoveEvent(disarmer, target);
            foreach (var occupant in _turf.GetEntitiesInTile(destCoords.Value, TileLookup))
                RaiseLocalEvent(occupant, ref preShove);

            var blocked = preShove.Solid
                || (destTile is { } dt && _turf.IsTileBlocked(dt, ShoveMask));

            if (blocked)
            {
                flags |= ShoveFlags.Blocked;
            }
            else
            {
                // Normal continuous-movement shove: throw them a tile back rather than tile-locking.
                // pushbackRatio 0 so only the target moves - the shover shouldn't recoil like a throw.
                _throwing.TryThrow(target, destCoords.Value, ShoveThrowSpeed, disarmer,
                    pushbackRatio: 0f, compensateFriction: true, doSpin: false, playSound: false);
            }
        }

        if ((flags & ShoveFlags.Blocked) == 0)
        {
            // BreakGrab(target); // only if you have grab-level escalation, downgrade it here
        }

        if ((flags & ShoveFlags.Blocked) != 0 && IsCardinal(shoveDir)
            && IsDirectionallyBlocked(targetCoords, destCoords.Value, shoveDir))
        {
            flags |= ShoveFlags.DirectionalBlocked;
        }

        if ((flags & ShoveFlags.CanHitSomething) != 0)
        {
            if ((flags & ShoveFlags.DirectionalBlocked) == 0)
            {
                var collide = new DisarmCollideEvent(disarmer, target, flags);
                foreach (var occupant in _turf.GetEntitiesInTile(destCoords.Value, TileLookup))
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
                _stun.TryKnockdown(target, KnockdownDaze, refresh: true);
                // recipient (disarmer) sees the "-user" line, everyone else the "-others" line.
                _popup.PopupPredicted(
                    Loc.GetString("disarm-knockdown-user", ("target", target)),
                    Loc.GetString("disarm-knockdown-others", ("user", disarmer), ("target", target)),
                    target, disarmer);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Low,
                    $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target} into something solid, knocking them down");
                return;
            }
        }

        if ((flags & ShoveFlags.CanKickSide) != 0)
        {
            _stun.TryAddParalyzeDuration(target, KickChainParalyze);
            // Stop the kick chaining forever: further shoves within this window stagger instead.
            var kicked = EnsureComp<StaggeredComponent>(target);
            kicked.NoSideKickUntil = _timing.CurTime + KickChainParalyze;
            Dirty(target, kicked);

            _popup.PopupPredicted(
                Loc.GetString("disarm-kick-user", ("target", target)),
                Loc.GetString("disarm-kick-others", ("user", disarmer), ("target", target)),
                target, disarmer);
            _adminLogger.Add(LogType.MeleeHit, LogImpact.Medium,
                $"{ToPrettyString(disarmer):disarmer} kicked {ToPrettyString(target):target} onto their side");
            return;
        }

        // General shove message - weapon variant if a weapon was used (SS13 "[ with weapon]").
        string shoveUser, shoveOthers;
        if (weapon is { } shoveWeapon)
        {
            shoveUser = Loc.GetString("disarm-shove-user-weapon", ("target", target), ("weapon", shoveWeapon));
            shoveOthers = Loc.GetString("disarm-shove-others-weapon", ("user", disarmer), ("target", target), ("weapon", shoveWeapon));
        }
        else
        {
            shoveUser = Loc.GetString("disarm-shove-user", ("target", target));
            shoveOthers = Loc.GetString("disarm-shove-others", ("user", disarmer), ("target", target));
        }
        _popup.PopupPredicted(shoveUser, shoveOthers, target, disarmer);

        if (_hands.TryGetActiveItem(target, out var heldItem))
        {
            var staggeredNow = TryComp<StaggeredComponent>(target, out var s) && s.StaggeredUntil > _timing.CurTime;
            var droppable = _tag.HasTag(heldItem.Value, DisarmDroppableTag);
            var lyingDown = !IsStanding(target);

            if ((staggeredNow && droppable) || lyingDown)
            {
                _hands.TryDrop(target, heldItem.Value);
                // recipient (the target, who lost the item) sees "You drop X", others "Y drops X".
                _popup.PopupPredicted(
                    Loc.GetString("disarm-drop-target", ("item", heldItem.Value)),
                    Loc.GetString("disarm-drop-others", ("target", target), ("item", heldItem.Value)),
                    target, target);
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

        // In tgstation CanKickSide/CanStagger come from a /mob/living/carbon override keyed on the
        // target's existing stagger. Reconstructed: already staggered (and not in the post-kick cooldown)
        // -> this shove chains into the kick; otherwise it starts/refreshes the stagger clock.
        if (TryComp<StaggeredComponent>(target, out var staggered)
            && staggered.StaggeredUntil > _timing.CurTime
            && staggered.NoSideKickUntil <= _timing.CurTime)
        {
            flags |= ShoveFlags.CanKickSide;
        }
        else
        {
            flags |= ShoveFlags.CanStagger;
        }

        return flags;
    }

    /// <summary>
    /// Whether the entity is currently on its feet. Entities with no <see cref="StandingStateComponent"/>
    /// at all (e.g. non-mobs) are treated as standing.
    /// </summary>
    private bool IsStanding(EntityUid uid)
    {
        return !_standingQuery.TryComp(uid, out var standing) || standing.Standing;
    }

    /// <summary>
    /// Whether the entity is currently buckled/seated (and so shouldn't be shoved into anything).
    /// </summary>
    private bool IsBuckled(EntityUid uid)
    {
        return _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled;
    }

    /// <summary>
    /// Grid + tile the entity currently occupies, or null if it isn't on a grid (open space). Used only
    /// for the "same tile" guard in <see cref="CanDisarm"/>.
    /// </summary>
    private (EntityUid Grid, Vector2i Indices)? GetTile(EntityUid uid)
    {
        if (!_xformQuery.TryComp(uid, out var xform)
            || xform.GridUid is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            return null;
        }

        return (gridUid, _map.TileIndicesFor(gridUid, grid, xform.Coordinates));
    }

    /// <summary>
    /// The tile one step over from <paramref name="uid"/> in <paramref name="dir"/>, plus its centre
    /// coordinates. Returns null (and null centre) if the entity isn't on a grid, or the destination has
    /// no tile (grid edge / space).
    /// </summary>
    private TileRef? GetAdjacentTile(EntityUid uid, Direction dir, out EntityCoordinates? center)
    {
        center = null;

        if (!_xformQuery.TryComp(uid, out var xform)
            || xform.GridUid is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            return null;
        }

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates) + DirToOffset(dir);
        center = _map.ToCenterCoordinates(gridUid, indices, grid);

        return _map.TryGetTileRef(gridUid, grid, indices, out var tileRef) ? tileRef : null;
    }

    private bool IsDirectionallyBlocked(EntityCoordinates fromTile, EntityCoordinates toTile, Direction shoveDir)
    {
        foreach (var ent in _turf.GetEntitiesInTile(fromTile, TileLookup))
            if (TryComp<DirectionalBlockerComponent>(ent, out var b) && b.BlocksFrom == shoveDir)
                return true;

        if (!fromTile.Equals(toTile))
            foreach (var ent in _turf.GetEntitiesInTile(toTile, TileLookup))
                if (TryComp<DirectionalBlockerComponent>(ent, out var b) && b.BlocksFrom == shoveDir.GetOpposite())
                    return true;

        return false;
    }

    private static bool IsCardinal(Direction dir) =>
        dir is Direction.North or Direction.South or Direction.East or Direction.West;

    private static Vector2i DirToOffset(Direction dir) => dir switch
    {
        Direction.East => new Vector2i(1, 0),
        Direction.NorthEast => new Vector2i(1, 1),
        Direction.North => new Vector2i(0, 1),
        Direction.NorthWest => new Vector2i(-1, 1),
        Direction.West => new Vector2i(-1, 0),
        Direction.SouthWest => new Vector2i(-1, -1),
        Direction.South => new Vector2i(0, -1),
        Direction.SouthEast => new Vector2i(1, -1),
        _ => new Vector2i(0, 0),
    };
}
