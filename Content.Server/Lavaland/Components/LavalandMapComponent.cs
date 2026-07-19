namespace Content.Server.Lavaland.Components;

/// <summary>
/// Marks the persistent Lavaland planet map and tracks ash storm timing.
/// </summary>
[RegisterComponent]
public sealed partial class LavalandMapComponent : Component
{
    /// <summary>
    /// When the next ash storm starts.
    /// </summary>
    [DataField]
    public TimeSpan NextStormTime;

    /// <summary>
    /// When the currently active ash storm ends. In the past when no storm is active.
    /// </summary>
    [DataField]
    public TimeSpan StormEndTime;

    /// <summary>
    /// Next time exposed mobs take storm damage.
    /// </summary>
    [DataField]
    public TimeSpan NextDamageTime;
}
