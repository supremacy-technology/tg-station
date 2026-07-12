using Robust.Shared.Utility;

namespace Content.Client.IconSmoothing;

/// <summary>
///     Draws an additional set of smoothed corner layers on top of an entity smoothed with
///     <see cref="IconSmoothComponent"/> in <see cref="IconSmoothingMode.Corners"/> mode.
///     The overlay layers keep their own <see cref="Color"/> instead of the base glass tint,
///     which is what window frames/edging use. Set the glass tint via
///     <see cref="IconSmoothComponent.Color"/> (not the sprite color) so it does not bleed
///     into this overlay.
/// </summary>
[RegisterComponent]
public sealed partial class SmoothEdgeOverlayComponent : Component
{
    /// <summary>
    ///     Allows child prototypes to opt out of an inherited overlay (e.g. windows with
    ///     pre-colored art that already has a frame baked in).
    /// </summary>
    [DataField]
    public bool Enabled = true;

    /// <summary>
    ///     RSI containing the overlay corner states, named "{base}0" through "{base}7" with
    ///     4 directions each, mirroring the base smoothing corner states.
    /// </summary>
    [DataField(required: true)]
    public ResPath Sprite;

    /// <summary>
    ///     Prefix of the overlay corner states within <see cref="Sprite"/>.
    /// </summary>
    [DataField("base")]
    public string StateBase = "edge";

    /// <summary>
    ///     Color the overlay layers are drawn with, independent of the base layers' tint.
    /// </summary>
    [DataField]
    public Color Color = Color.White;
}
