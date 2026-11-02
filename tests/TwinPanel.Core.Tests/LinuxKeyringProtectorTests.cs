// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;
using TwinPanel.Core.Remote;

namespace TwinPanel.Core.Tests;

public class LinuxKeyringProtectorTests
{
    /// <summary>Remplace le trousseau du bureau : même réponses que secret-tool.</summary>
    private sealed class FakeKeyring
    {
        public string? Stored;
        public bool Running = true;
        public bool RefusesToStore;
        public List<string[]> Calls = [];

        public KeyringToolResult Run(string[] arguments, string? input)
        {
            Calls.Add(arguments);
            if (!Running) return new KeyringToolResult(1, "", "Cannot autolaunch D-Bus");
            if (arguments[0] == "store")
            {
                if (RefusesToStore) return new KeyringToolResult(1, "", "refused");
                Stored = input;
                return new KeyringToolResult(0, "", "");
            }
            return Stored is null ? new KeyringToolResult(1, "", "") : new KeyringToolResult(0, Stored + "\n", "");
        }
    }

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes("un secret");

    [Fact]
    public void The_first_use_creates_a_key_and_later_uses_find_it()
    {
        var keyring = new FakeKeyring();
        var protectedData = new LinuxKeyringProtector(keyring.Run).Protect(Secret);
        Assert.NotNull(keyring.Stored);
        Assert.Equal(Secret, new LinuxKeyringProtector(keyring.Run).Unprotect(protectedData));
    }

    [Fact]
    public void The_key_never_appears_on_a_command_line()
    {
        var keyring = new FakeKeyring();
        new LinuxKeyringProtector(keyring.Run).Protect(Secret);
        Assert.DoesNotContain(keyring.Calls.SelectMany(c => c), a => a.Contains(keyring.Stored!, StringComparison.Ordinal));
    }

    [Fact]
    public void Nothing_readable_is_left_in_the_result()
    {
        var keyring = new FakeKeyring();
        var result = new LinuxKeyringProtector(keyring.Run).Protect(Secret);
        Assert.DoesNotContain("un secret", Encoding.UTF8.GetString(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_keyring_it_is_unavailable_and_creates_nothing()
    {
        var keyring = new FakeKeyring { Running = false };
        var protector = new LinuxKeyringProtector(keyring.Run);
        Assert.False(protector.IsAvailable);
        Assert.Null(keyring.Stored);
        Assert.Throws<VaultKeyringException>(() => protector.Protect(Secret));
    }

    [Fact]
    public void A_key_that_cannot_be_saved_is_not_used()
    {
        Assert.False(new LinuxKeyringProtector(new FakeKeyring { RefusesToStore = true }.Run).IsAvailable);
    }

    [Theory]
    [InlineData("pas du base64 !")]
    [InlineData("dHJvcCBjb3VydA==")]
    public void A_damaged_key_is_refused_and_not_replaced(string stored)
    {
        var keyring = new FakeKeyring { Stored = stored };
        Assert.False(new LinuxKeyringProtector(keyring.Run).IsAvailable);
        Assert.Equal(stored, keyring.Stored);
    }

    [Fact]
    public void Data_from_another_key_gives_a_clear_error()
    {
        var protectedData = new LinuxKeyringProtector(new FakeKeyring().Run).Protect(Secret);
        Assert.Throws<VaultKeyringException>(() => new LinuxKeyringProtector(new FakeKeyring().Run).Unprotect(protectedData));
    }

    [Fact]
    public void A_refused_keyring_is_not_asked_again_at_once()
    {
        var keyring = new FakeKeyring { Running = false };
        var protector = new LinuxKeyringProtector(keyring.Run);
        Assert.False(protector.IsAvailable);
        int calls = keyring.Calls.Count;
        Assert.False(protector.IsAvailable);
        Assert.Equal(calls, keyring.Calls.Count);
    }
}
