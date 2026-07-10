using System.Diagnostics.CodeAnalysis;
using Content.Shared.Administration.Logs;
using Content.Shared.Buckle.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Database;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Maps;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Tag;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Disarming.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared.Weapons.Melee.Disarming.Systems;

/// <summary>
/// SS13-style shove/disarm ("disable"), following the tgstation wiki rules, ported to normal SS14
/// continuous movement. Right-click with an empty hand shoves the target one tile away:
///
///  - Open tile:            slowed (staggered) for 3s. A second shove while slowed knocks a ranged
///                          weapon out of their active hand instead.
///  - Blocking tile = table: pushed onto it and knocked over for 3s.
///  - Blocking tile = mob:  both fall - the target for 3s, the collateral victim for 1.1s.
///  - Blocking tile = else: knocked down for 3s.
///  - Target already down:  paralyzed for 3s (cannot be chained or extended).
///  - Can't shove someone standing on your own tile.
/// </summary>
public sealed class DisarmingSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
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
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;

    [Dependency] private readonly EntityQuery<StandingStateComponent> _standingQuery = default!;
    [Dependency] private readonly EntityQuery<BuckleComponent> _buckleQuery = default!;
    [Dependency] private readonly EntityQuery<TransformComponent> _xformQuery = default!;
    [Dependency] private readonly EntityQuery<MapGridComponent> _gridQuery = default!;

    // Walls + mobs; matches what a mob itself collides against, so "blocked" means "shoved into
    // something they couldn't have walked through".
    private const CollisionGroup ShoveMask = CollisionGroup.MobMask;
    private const float ShoveThrowSpeed = 5f;
    // How many tiles a successful shove pushes the target (wiki = 1). Tune to taste; the throw still
    // collides with walls. Table-landing only ever uses the immediately-adjacent tile.
    private const int ShoveTiles = 1;
    private const LookupFlags TileLookup = LookupFlags.Dynamic | LookupFlags.Static;

    // "slowed down very slightly" - multiplier applied to walk & sprint speed while staggered.
    private const float StaggerSpeed = 0.9f;

    private static readonly ProtoId<TagPrototype> DisarmDroppableTag = "DisarmDroppable";

    // Wiki plays a shove sound;
    private static readonly SoundSpecifier ShoveSound = new SoundPathSpecifier("/Audio/Weapons/shove.ogg");

    private readonly List<EntityUid> _expired = new();

    private static readonly TimeSpan StaggerTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KnockdownTime = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CollateralKnockdownTime = TimeSpan.FromSeconds(1.1);
    private static readonly TimeSpan ParalyzeTime = TimeSpan.FromSeconds(3);

    public override void Initialize()
    {
        base.Initialize();

        // Staggered = a small movement slowdown that expires on its own.
        SubscribeLocalEvent<StaggeredComponent, RefreshMovementSpeedModifiersEvent>(OnStaggerRefresh);
        SubscribeLocalEvent<StaggeredComponent, ComponentStartup>(OnStaggerStartup);
        SubscribeLocalEvent<StaggeredComponent, ComponentShutdown>(OnStaggerShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Expire staggers on the server; removal networks to the client, and the ComponentShutdown
        // handler refreshes movement speed on both sides.
        if (!_net.IsServer)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<StaggeredComponent>();
        while (query.MoveNext(out var uid, out var stagger))
        {
            if (stagger.StaggeredUntil <= now)
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
            RemComp<StaggeredComponent>(uid);
        _expired.Clear();
    }

    private void OnStaggerRefresh(EntityUid uid, StaggeredComponent component, RefreshMovementSpeedModifiersEvent args)
    {
        if (component.StaggeredUntil > _timing.CurTime)
            args.ModifySpeed(StaggerSpeed);
    }

    private void OnStaggerStartup(EntityUid uid, StaggeredComponent component, ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void OnStaggerShutdown(EntityUid uid, StaggeredComponent component, ComponentShutdown args)
    {
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    /// <summary>
    /// Universal conditions for a shove: the disarmer is on their feet, isn't shoving themselves, and
    /// isn't standing on the exact same tile as the target (there'd be no direction to shove them).
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

        var hitEv = new DisarmHitEvent(disarmer, weapon);
        RaiseLocalEvent(target, ref hitEv);

        // Shove feedback sound (server-authoritative - this whole method only runs on the server, so a
        // predicted sound would be suppressed for the shover; PlayPvs reaches everyone including them).
        _audio.PlayPvs(ShoveSound, target);

        // ── Target is already knocked down (any source, incl. slipping) -> paralyze for 3s. This cannot
        //    be chained or extended, so a shove while already paralyzed/stunned does nothing. ──
        if (HasComp<KnockedDownComponent>(target))
        {
            if (!HasComp<StunnedComponent>(target))
            {
                _stun.TryUpdateParalyzeDuration(target, ParalyzeTime);
                ShovePopup(
                    Loc.GetString("disarm-paralyze-user", ("target", target)),
                    Loc.GetString("disarm-paralyze-others", ("user", disarmer), ("target", target)),
                    target, disarmer);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Medium,
                    $"{ToPrettyString(disarmer):disarmer} paralyzed downed {ToPrettyString(target):target} with a shove");
            }
            return;
        }

        // Direction from the disarmer to the target, rotated into the target's grid frame so the tile
        // step lines up with what the player sees even on a rotated grid.
        var targetXform = Transform(target);
        var delta = _transform.GetMapCoordinates(target).Position - _transform.GetMapCoordinates(disarmer).Position;
        var gridRot = targetXform.GridUid is { } shoveGrid ? _transform.GetWorldRotation(shoveGrid) : Angle.Zero;
        var shoveDir = (-gridRot).RotateVec(delta).GetDir();

        var targetCoords = targetXform.Coordinates;

        // The tile immediately in the shove direction (may be null off-grid / at a grid edge).
        var destTile = GetTileAway(target, shoveDir, 1, out var destCoords);
        destCoords ??= targetCoords;

        var force = CompOrNull<ShoveStatsComponent>(disarmer)?.MoveForce ?? 1f;
        var resist = CompOrNull<ShoveStatsComponent>(target)?.MoveResist ?? 1f;
        var canMove = force >= resist;

        if (canMove && !IsBuckled(target))
        {
            // ── Blocking tile has a TABLE -> push them onto it, knocked over for 3s. ──
            /*if (!IsClimbing(target)
                && HasComp<ClimbingComponent>(target)
                && TryGetClimbableAt(destCoords.Value, target, out var climbable))
            {
                // Snap onto the table tile first so Climb() registers in-place (no multi-tick glide that
                // the knockdown would cut short), then drop them onto their back on it.
                _transform.SetCoordinates(target, destCoords.Value);
                _climb.Climb(target, disarmer, climbable.Value, silent: false);
                _stun.TryKnockdown(target, KnockdownTime, refresh: true);
                ShovePopup(
                    Loc.GetString("disarm-table-user", ("target", target)),
                    Loc.GetString("disarm-table-others", ("user", disarmer), ("target", target)),
                    target, disarmer);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Low,
                    $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target} onto {ToPrettyString(climbable.Value):climbable}");
                return;
            }*/

            // ── Blocking tile has ANOTHER MOB -> both fall (target 3s, collateral 1.1s). ──
            if (TryGetShoveMob(destCoords.Value, target, out var other))
            {
                _stun.TryKnockdown(target, KnockdownTime, refresh: true);
                _stun.TryKnockdown(other.Value, CollateralKnockdownTime, refresh: true);
                ShovePopup(
                    Loc.GetString("disarm-slam-user", ("target", target), ("victim", other.Value)),
                    Loc.GetString("disarm-slam-others", ("user", disarmer), ("target", target), ("victim", other.Value)),
                    target, disarmer);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Medium,
                    $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target} into {ToPrettyString(other.Value):victim}, knocking both down");
                return;
            }

            // ── Blocking tile is solid (wall etc) -> knocked down for 3s. ──
            if (destTile is { } dt && _turf.IsTileBlocked(dt, ShoveMask))
            {
                _stun.TryKnockdown(target, KnockdownTime, refresh: true);
                ShovePopup(
                    Loc.GetString("disarm-knockdown-user", ("target", target)),
                    Loc.GetString("disarm-knockdown-others", ("user", disarmer), ("target", target)),
                    target, disarmer);
                _adminLogger.Add(LogType.MeleeHit, LogImpact.Low,
                    $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target} into something solid, knocking them down");
                return;
            }

            // ── Open tile -> push them one tile. ──
            GetTileAway(target, shoveDir, ShoveTiles, out var far);
            _throwing.TryThrow(target, far ?? destCoords.Value, ShoveThrowSpeed, disarmer,
                pushbackRatio: 0f, compensateFriction: true, doSpin: false, playSound: false);
        }

        // ── Open outcome: first shove slows them; a shove while already slowed knocks their ranged
        //    weapon out of hand instead. ──
        var wasStaggered = IsStaggered(target);
        Stagger(target);

        // Wiki: a shove while slowed knocks a *ranged weapon* out of hand. Any GunComponent counts, so
        // every gun works without tagging; the DisarmDroppable tag covers anything else you want droppable.
        if (wasStaggered
            && _hands.TryGetActiveItem(target, out var heldItem)
            && (HasComp<GunComponent>(heldItem.Value) || _tag.HasTag(heldItem.Value, DisarmDroppableTag)))
        {
            _hands.TryDrop(target, heldItem.Value);
            ShovePopup(
                Loc.GetString("disarm-drop-target", ("item", heldItem.Value)),
                Loc.GetString("disarm-drop-others", ("target", target), ("item", heldItem.Value)),
                target, target);
        }
        else
        {
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
            ShovePopup(shoveUser, shoveOthers, target, disarmer);
        }

        _adminLogger.Add(LogType.MeleeHit, LogImpact.Low, $"{ToPrettyString(disarmer):disarmer} shoved {ToPrettyString(target):target}");
    }

    /// <summary>Applies (or refreshes) the 3-second stagger slowdown.</summary>
    private void Stagger(EntityUid uid)
    {
        var stagger = EnsureComp<StaggeredComponent>(uid);
        stagger.StaggeredUntil = _timing.CurTime + StaggerTime;
        Dirty(uid, stagger);
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private bool IsStaggered(EntityUid uid)
    {
        return TryComp<StaggeredComponent>(uid, out var stagger) && stagger.StaggeredUntil > _timing.CurTime;
    }

    /// <summary>
    /// Whether the entity is currently on its feet. Entities with no <see cref="StandingStateComponent"/>
    /// at all (e.g. non-mobs) are treated as standing.
    /// </summary>
    private bool IsStanding(EntityUid uid)
    {
        return !_standingQuery.TryComp(uid, out var standing) || standing.Standing;
    }

    private bool IsBuckled(EntityUid uid)
    {
        return _buckleQuery.TryComp(uid, out var buckle) && buckle.Buckled;
    }

    /// <summary>Whether the entity is currently up on a table/climbable.</summary>
    private bool IsClimbing(EntityUid uid)
    {
        return TryComp<ClimbingComponent>(uid, out var climbing) && climbing.IsClimbing;
    }

    /// <summary>
    /// visible_message/to_chat split: <paramref name="recipient"/> sees their own line, everyone else in
    /// PVS sees the third-person line. Server popups (not PopupPredicted) because TryDisarm is server-only.
    /// </summary>
    private void ShovePopup(string recipientMessage, string othersMessage, EntityUid uid, EntityUid recipient)
    {
        _popup.PopupEntity(recipientMessage, uid, recipient);
        _popup.PopupEntity(othersMessage, uid, Filter.PvsExcept(recipient, entityManager: EntityManager), true);
    }

    /// <summary>First climbable (table, altar, ...) on the tile, if any.</summary>
    private bool TryGetClimbableAt(EntityCoordinates coords, EntityUid self, [NotNullWhen(true)] out EntityUid? climbable)
    {
        climbable = null;
        foreach (var ent in _turf.GetEntitiesInTile(coords, TileLookup))
        {
            if (ent == self)
                continue;

            if (HasComp<ClimbableComponent>(ent))
            {
                climbable = ent;
                return true;
            }
        }

        return false;
    }

    /// <summary>First standing mob (other than <paramref name="self"/>) on the tile - the collateral victim.</summary>
    private bool TryGetShoveMob(EntityCoordinates coords, EntityUid self, [NotNullWhen(true)] out EntityUid? mob)
    {
        mob = null;
        foreach (var ent in _turf.GetEntitiesInTile(coords, TileLookup))
        {
            if (ent == self)
                continue;

            if (HasComp<MobStateComponent>(ent) && IsStanding(ent))
            {
                mob = ent;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Grid + tile the entity occupies, or null if off-grid. Used only for the "same tile" guard.
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
    /// The tile <paramref name="tiles"/> steps over from <paramref name="uid"/> in <paramref name="dir"/>,
    /// plus its centre coordinates. <paramref name="center"/> is set whenever the entity is on a grid;
    /// the returned <see cref="TileRef"/> is null if there is no tile there (grid edge / space).
    /// </summary>
    private TileRef? GetTileAway(EntityUid uid, Direction dir, int tiles, out EntityCoordinates? center)
    {
        center = null;

        if (!_xformQuery.TryComp(uid, out var xform)
            || xform.GridUid is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            return null;
        }

        var indices = _map.TileIndicesFor(gridUid, grid, xform.Coordinates) + DirToOffset(dir) * tiles;
        center = _map.ToCenterCoordinates(gridUid, indices, grid);

        return _map.TryGetTileRef(gridUid, grid, indices, out var tileRef) ? tileRef : null;
    }

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
