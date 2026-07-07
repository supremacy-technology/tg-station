using System.Numerics;
using Content.Shared.Movement.TileMovement.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.TileMovement.Systems;

/// <summary>
/// Ведёт "тайл-локнутых" мобов (см. TileMoverComponent).
///
/// Поток данных:
///  1. Штатный InputMoverComponent + стандартный ввод SS14 продолжают заполнять
///     HeldMoveButtons как обычно (мы это не трогаем).
///  2. Когда HeldMoveButtons меняется, движок бросает MoveInputEvent — мы на него подписаны.
///  3. Если мы не в процессе шага — проверяем целевой тайл и, если он свободен,
///     запускаем интерполяцию EntityCoordinates от текущего тайла к целевому.
///  4. Если мы уже в процессе шага — буферизуем направление и применяем его сразу
///     по завершении текущего шага (отзывчивость без рывков).
///
/// ВАЖНО: чтобы это работало, стандартный SharedMoverController.HandleMobMovement
/// должен игнорировать сущности с TileMoverComponent (см. патч в README.md) —
/// иначе физический контроллер продолжит одновременно двигать тело.
/// </summary>
public sealed class TileMovementSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;

    [Dependency] private readonly EntityQuery<PhysicsComponent> _physicsQuery = default!;
    [Dependency] private readonly EntityQuery<MapGridComponent> _gridQuery = default!;
    [Dependency] private readonly EntityQuery<TransformComponent> _xformQuery = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TileMoverComponent, MoveInputEvent>(OnMoveInput);
    }

    private void OnMoveInput(Entity<TileMoverComponent> ent, ref MoveInputEvent args)
    {
        var buttons = args.Entity.Comp.HeldMoveButtons;
        var offset = ButtonsToOffset(buttons);
        var sprinting = IsSprinting(buttons);

        if (ent.Comp.IsMoving)
        {
            // Уже идёт шаг — просто запоминаем последнее нажатое направление.
            ent.Comp.HasQueuedStep = offset != Vector2i.Zero;
            ent.Comp.QueuedOffset = offset;
            ent.Comp.QueuedSprinting = sprinting;
            Dirty(ent);
            return;
        }

        if (offset != Vector2i.Zero)
            TryStartStep(ent, offset, sprinting);
    }

    /// <summary>В SS14 Walk-бит выставлен, когда игрок идёт пешком (не спринтует) —
    /// см. InputMoverComponent.Sprinting в исходнике движка.</summary>
    private static bool IsSprinting(MoveButtons buttons) => (buttons & MoveButtons.Walk) == 0;

    /// <summary>Переводит зажатые кнопки в смещение тайла. Даёт и диагонали (как в SS13),
    /// если зажаты две смежные кнопки одновременно.</summary>
    private static Vector2i ButtonsToOffset(MoveButtons buttons)
    {
        var x = 0;
        var y = 0;

        if ((buttons & MoveButtons.Up) != 0) y += 1;
        if ((buttons & MoveButtons.Down) != 0) y -= 1;
        if ((buttons & MoveButtons.Left) != 0) x -= 1;
        if ((buttons & MoveButtons.Right) != 0) x += 1;

        return new Vector2i(x, y);
    }

    private bool TryStartStep(Entity<TileMoverComponent> ent, Vector2i offset, bool sprinting)
    {
        if (!_xformQuery.TryComp(ent.Owner, out var xform)
            || xform.GridUid is not { } gridUid
            || !_gridQuery.TryComp(gridUid, out var grid))
        {
            return false; // не на сетке (открытый космос) — тайл-лок тут не применяем
        }

        var current = _mapSystem.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var target = current + offset;

        if (!CanEnterTile(gridUid, grid, target, ent.Owner))
            return false;

        ent.Comp.FromTile = current;
        ent.Comp.ToTile = target;
        ent.Comp.Progress = 0f;
        ent.Comp.IsMoving = true;
        ent.Comp.HasQueuedStep = false;
        ent.Comp.CurrentStepTime = sprinting ? ent.Comp.StepTimeSprinting : ent.Comp.StepTimeWalking;
        Dirty(ent);
        return true;
    }

    /// <summary>
    /// Упрощённая проверка препятствия для прототипа: тайл должен физически
    /// существовать (не пустота открытого космоса) и не содержать других
    /// "твёрдых" физических тел (PhysicsComponent.Hard).
    ///
    /// Это НЕ полноценная проверка по CollisionLayer/CollisionMask мувера —
    /// для прототипа этого достаточно, а закрытые двери и стены и так помечены
    /// Hard = true, так что дополнительно их обрабатывать не нужно.
    /// </summary>
    private bool CanEnterTile(EntityUid gridUid, MapGridComponent grid, Vector2i tile, EntityUid self)
    {
        if (!_mapSystem.TryGetTileRef(gridUid, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty)
            return false;

        var intersecting = _lookup.GetLocalEntitiesIntersecting(gridUid, tile);

        foreach (var other in intersecting)
        {
            if (other == self)
                continue;

            if (_physicsQuery.TryComp(other, out var physics) && physics.CanCollide && physics.Hard)
                return false;
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TileMoverComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.IsMoving)
                continue;

            if (!_xformQuery.TryComp(uid, out var xform)
                || xform.GridUid is not { } gridUid
                || !_gridQuery.TryComp(gridUid, out var grid))
            {
                comp.IsMoving = false;
                Dirty(uid, comp);
                continue;
            }

            comp.Progress += frameTime / comp.CurrentStepTime;

            if (comp.Progress >= 1f)
            {
                var center = _mapSystem.ToCenterCoordinates(gridUid, comp.ToTile, grid);
                _transform.SetCoordinates(uid, center);

                comp.IsMoving = false;
                comp.Progress = 0f;
                Dirty(uid, comp);

                // Уже нажато следующее направление — продолжаем без паузы в Idle.
                if (comp.HasQueuedStep)
                {
                    comp.HasQueuedStep = false;
                    TryStartStep((uid, comp), comp.QueuedOffset, comp.QueuedSprinting);
                }

                continue;
            }

            var from = _mapSystem.ToCenterCoordinates(gridUid, comp.FromTile, grid);
            var to = _mapSystem.ToCenterCoordinates(gridUid, comp.ToTile, grid);
            var lerped = Vector2.Lerp(from.Position, to.Position, comp.Progress);

            _transform.SetCoordinates(uid, new EntityCoordinates(gridUid, lerped));
            Dirty(uid, comp);
        }
    }
}
