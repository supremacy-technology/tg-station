using Robust.Shared.GameStates;
namespace Content.Shared.Movement.TileMovement.Components;

/// <summary>
/// Помечает сущность как использующую дискретное, тайл-локнутое перемещение
/// (как в SS13) вместо штатного непрерывного физического движения.
///
/// Требования:
///  - На сущности должен также быть <see cref="Content.Shared.Movement.Components.InputMoverComponent"/>
///    (для захвата ввода и работы прогнозирования) — TileMoverComponent сам по себе
///    ввод не читает, он подписывается на MoveInputEvent, который InputMoverComponent
///    и порождает.
///  - Нужен патч в SharedMoverController.HandleMobMovement, который пропускает
///    сущности с этим компонентом (см. README.md в этой поставке) — иначе штатный
///    контроллер продолжит параллельно двигать физику и получится "двойное" движение.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TileMoverComponent : Component
{
    /// <summary>Тайл, с которого начался текущий шаг.</summary>
    [DataField, AutoNetworkedField]
    public Vector2i FromTile;

    /// <summary>Тайл, в который сейчас идёт шаг (или в котором мы стоим, если IsMoving == false).</summary>
    [DataField, AutoNetworkedField]
    public Vector2i ToTile;

    /// <summary>0 — только начали шаг, 1 — шаг завершён.</summary>
    [DataField, AutoNetworkedField]
    public float Progress;

    [DataField, AutoNetworkedField]
    public bool IsMoving;

    /// <summary>Длительность текущего шага в секундах — выставляется при старте шага
    /// в зависимости от того, зажат ли спринт (см. TileMovementSystem.IsSprinting).</summary>
    [DataField, AutoNetworkedField]
    public float CurrentStepTime = 0.32f;

    [DataField, AutoNetworkedField]
    public float StepTimeWalking = 0.32f;

    [DataField, AutoNetworkedField]
    public float StepTimeSprinting = 0.2f;

    /// <summary>Направление, зажатое во время текущего шага — применяется сразу
    /// после его завершения, без ожидания в Idle (буферизация ввода).</summary>
    [DataField, AutoNetworkedField]
    public bool HasQueuedStep;

    [DataField, AutoNetworkedField]
    public Vector2i QueuedOffset;

    /// <summary>Спринтили ли в момент, когда был запомнен буферизованный шаг.</summary>
    [DataField, AutoNetworkedField]
    public bool QueuedSprinting;
}
