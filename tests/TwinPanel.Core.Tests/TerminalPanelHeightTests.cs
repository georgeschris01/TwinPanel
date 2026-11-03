// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Terminal;
using Xunit;

namespace TwinPanel.Core.Tests;

public class TerminalPanelHeightTests
{
    [Fact]
    public void Before_the_window_has_its_size_the_chosen_height_is_kept() =>
        Assert.Equal(380, TerminalPanelHeight.ToApply(380, 0));

    [Fact]
    public void A_laid_out_window_limits_the_height_to_leave_room_for_the_files()
    {
        Assert.Equal(380, TerminalPanelHeight.ToApply(380, 1300));
        Assert.Equal(300 - TerminalPanelHeight.MinFiles, TerminalPanelHeight.ToApply(380, 300));
        Assert.Equal(TerminalPanelHeight.MinTerminal, TerminalPanelHeight.ToApply(50, 1300));
    }

    [Fact]
    public void A_panel_not_yet_measured_does_not_overwrite_the_chosen_height()
    {
        // À la réouverture des terminaux, le panneau est ouvert puis refermé avant d'être mesuré.
        Assert.Equal(380, TerminalPanelHeight.ToRemember(0, 380));
        Assert.Equal(250, TerminalPanelHeight.ToRemember(250, 380));
    }

    [Fact]
    public void A_missing_height_falls_back_to_the_default() =>
        Assert.Equal(260, TerminalPanelHeight.ToApply(0, 0));
}
