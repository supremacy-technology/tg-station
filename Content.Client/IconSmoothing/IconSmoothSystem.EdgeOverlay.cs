using Robust.Client.GameObjects;
using Robust.Shared.Utility;
using static Robust.Client.GameObjects.SpriteComponent;

namespace Content.Client.IconSmoothing;

public sealed partial class IconSmoothSystem
{
    // Handles the separately-colored edging overlay drawn on top of corner-smoothed sprites
    // (see SmoothEdgeOverlayComponent).

    private void InitializeEdgeOverlay()
    {
        SubscribeLocalEvent<SmoothEdgeOverlayComponent, ComponentShutdown>(OnEdgeOverlayShutdown);
    }

    private void OnEdgeOverlayShutdown(EntityUid uid, SmoothEdgeOverlayComponent component, ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        RemoveEdgeOverlayLayers((uid, sprite));
    }

    private void RemoveEdgeOverlayLayers(Entity<SpriteComponent?> sprite)
    {
        _sprite.LayerMapRemove(sprite, EdgeOverlayCorners.SE);
        _sprite.LayerMapRemove(sprite, EdgeOverlayCorners.NE);
        _sprite.LayerMapRemove(sprite, EdgeOverlayCorners.NW);
        _sprite.LayerMapRemove(sprite, EdgeOverlayCorners.SW);
    }

    /// <summary>
    ///     (Re)creates the four overlay corner layers. Must be called after the base corner
    ///     layers have been set up so the overlay draws above them.
    /// </summary>
    private void SetEdgeOverlayLayers(Entity<SpriteComponent?> sprite, SmoothEdgeOverlayComponent component)
    {
        RemoveEdgeOverlayLayers(sprite);

        if (!component.Enabled)
            return;

        var state0 = new SpriteSpecifier.Rsi(component.Sprite, $"{component.StateBase}0");
        AddEdgeOverlayLayer(sprite, EdgeOverlayCorners.SE, state0, DirectionOffset.None, component.Color);
        AddEdgeOverlayLayer(sprite, EdgeOverlayCorners.NE, state0, DirectionOffset.CounterClockwise, component.Color);
        AddEdgeOverlayLayer(sprite, EdgeOverlayCorners.NW, state0, DirectionOffset.Flip, component.Color);
        AddEdgeOverlayLayer(sprite, EdgeOverlayCorners.SW, state0, DirectionOffset.Clockwise, component.Color);
    }

    private void AddEdgeOverlayLayer(Entity<SpriteComponent?> sprite, EdgeOverlayCorners key, SpriteSpecifier.Rsi state, DirectionOffset offset, Color color)
    {
        _sprite.LayerMapSet(sprite, key, _sprite.AddLayer(sprite, state));
        _sprite.LayerSetDirOffset(sprite, key, offset);
        _sprite.LayerSetColor(sprite, key, color);
    }

    /// <summary>
    ///     Mirrors the corner states calculated for the base layers onto the overlay layers.
    /// </summary>
    private void UpdateEdgeOverlay(Entity<SpriteComponent> sprite, CornerFill ne, CornerFill nw, CornerFill sw, CornerFill se)
    {
        if (!TryComp<SmoothEdgeOverlayComponent>(sprite.Owner, out var overlay) || !overlay.Enabled)
            return;

        _sprite.LayerSetRsiState(sprite.AsNullable(), EdgeOverlayCorners.NE, $"{overlay.StateBase}{(int)ne}");
        _sprite.LayerSetRsiState(sprite.AsNullable(), EdgeOverlayCorners.SE, $"{overlay.StateBase}{(int)se}");
        _sprite.LayerSetRsiState(sprite.AsNullable(), EdgeOverlayCorners.SW, $"{overlay.StateBase}{(int)sw}");
        _sprite.LayerSetRsiState(sprite.AsNullable(), EdgeOverlayCorners.NW, $"{overlay.StateBase}{(int)nw}");
    }

    private enum EdgeOverlayCorners : byte
    {
        SE,
        NE,
        NW,
        SW,
    }
}
