// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Settings;

namespace TwinPanel.Core.Tests;

public class DropActionTests
{
    [Theory]
    [InlineData(DropAction.Ask, null)]
    [InlineData(DropAction.Copy, false)]
    [InlineData(DropAction.Move, true)]
    public void Without_key_the_option_decides(DropAction setting, bool? move) =>
        Assert.Equal(move, DropActions.Resolve(setting, shift: false, control: false));

    [Theory]
    [InlineData(DropAction.Ask)]
    [InlineData(DropAction.Copy)]
    [InlineData(DropAction.Move)]
    public void Shift_always_moves_and_ctrl_always_copies(DropAction setting)
    {
        Assert.True(DropActions.Resolve(setting, shift: true, control: false));
        Assert.False(DropActions.Resolve(setting, shift: false, control: true));
    }

    [Fact]
    public void Old_settings_keep_asking_and_open_the_dropped_tab()
    {
        Assert.Equal(DropAction.Ask, new BehaviorSettings().DropAction);
        Assert.True(new BehaviorSettings().OpenTabOnDrop);
        Assert.True(new BehaviorSettings().OpenTabOnHover);
    }
}
