using JetBrains.Annotations;

namespace Content.Client.Clothing.ModSuit;

/// <summary>
///     Opens the <see cref="ModSuitPanelWindow"/> MOD interface panel.
/// </summary>
[UsedImplicitly]
public sealed class ModSuitPanelBoundUserInterface : BoundUserInterface
{
    private ModSuitPanelWindow? _window;

    public ModSuitPanelBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = new ModSuitPanelWindow(EntMan, Owner);
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}
