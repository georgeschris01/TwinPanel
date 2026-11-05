// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using TwinPanel.Core.Remote;
using TwinPanel.Core.Settings;
using TwinPanel.Core.Terminal;

namespace TwinPanel.Core.Tests;

/// <summary>Mot de passe maître oublié : code de secours, remise à zéro ; et commandes secrètes du terminal.</summary>
public class VaultRecoveryTests
{
    private static SecretVault NewVault(TempDir t, bool keyring = true) =>
        new(Path.Combine(t.Path, "secrets.dat"), new FakeProtector(keyring));

    [Fact]
    public void Recovery_codes_mix_letters_digits_and_special_characters_in_groups_of_four()
    {
        var code = RecoveryCodes.New();
        Assert.Equal(RecoveryCodes.Length + RecoveryCodes.Length / 4 - 1, code.Length);
        Assert.All(code.Split('-'), g => Assert.Equal(4, g.Length));
        Assert.True(RecoveryCodes.IsWellFormed(code));
        Assert.NotEqual(code, RecoveryCodes.New());
        // Sans caractères faciles à confondre en recopiant un papier.
        Assert.DoesNotContain(RecoveryCodes.Alphabet, c => c is '0' or 'O' or '1' or 'I' or 'L' or '-' or ' ');
        Assert.Contains(RecoveryCodes.Alphabet, char.IsLetter);
        Assert.Contains(RecoveryCodes.Alphabet, char.IsDigit);
        Assert.Contains(RecoveryCodes.Alphabet, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void A_recovery_code_is_typed_as_one_likes()
    {
        Assert.Equal("AB#C2%", RecoveryCodes.Normalize(" ab#c - 2% "));
        Assert.False(RecoveryCodes.IsWellFormed("trop-court"));
        Assert.False(RecoveryCodes.IsWellFormed(new string('O', RecoveryCodes.Length))); // pas dans l'alphabet
    }

    [Fact]
    public void A_forgotten_master_password_is_replaced_with_the_recovery_code_without_losing_anything()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        vault.Set("pwd:1", "mot-de-passe-du-serveur");
        vault.SetMasterPassword("ancien-maitre");
        Assert.False(vault.HasRecoveryCode);
        var code = vault.CreateRecoveryCode();
        Assert.True(vault.HasRecoveryCode);
        Assert.DoesNotContain(RecoveryCodes.Normalize(code), File.ReadAllText(Path.Combine(t.Path, "secrets.dat")));

        var reopened = NewVault(t);
        Assert.False(reopened.IsUnlocked);
        Assert.False(reopened.UnlockWithRecoveryCode(RecoveryCodes.New()));
        Assert.False(reopened.IsUnlocked);
        Assert.True(reopened.UnlockWithRecoveryCode(code.ToLowerInvariant().Replace("-", " ")));
        Assert.Equal("mot-de-passe-du-serveur", reopened.Get("pwd:1"));

        // Nouveau mot de passe maître : l'ancien code ne marche plus.
        reopened.SetMasterPassword("nouveau-maitre");
        Assert.False(reopened.HasRecoveryCode);
        var again = NewVault(t);
        Assert.False(again.UnlockWithRecoveryCode(code));
        Assert.True(again.Unlock("nouveau-maitre"));
        Assert.Equal("mot-de-passe-du-serveur", again.Get("pwd:1"));
    }

    [Fact]
    public void A_new_recovery_code_replaces_the_old_one()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        vault.SetMasterPassword("un-maitre-de-test");
        var first = vault.CreateRecoveryCode();
        var second = vault.CreateRecoveryCode();
        var reopened = NewVault(t);
        Assert.False(reopened.UnlockWithRecoveryCode(first));
        Assert.True(reopened.UnlockWithRecoveryCode(second));
    }

    [Fact]
    public void A_recovery_code_needs_a_master_password_and_an_open_vault()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        Assert.Throws<InvalidOperationException>(() => vault.CreateRecoveryCode());
        vault.SetMasterPassword("un-maitre-de-test");
        vault.Lock();
        Assert.Throws<VaultLockedException>(() => vault.CreateRecoveryCode());
    }

    [Fact]
    public void Starting_over_erases_the_secrets_but_keeps_a_copy_of_the_old_vault()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        vault.Set("pwd:1", "mot-de-passe-du-serveur");
        vault.SetMasterPassword("oublie-pour-de-bon");
        vault.AutoLockMinutes = 30;
        var reopened = NewVault(t);

        var kept = reopened.ResetForgotten();

        Assert.NotNull(kept);
        Assert.True(File.Exists(kept));
        Assert.False(reopened.HasMasterPassword);
        Assert.True(reopened.IsUnlocked);
        Assert.Null(reopened.Get("pwd:1"));
        Assert.Equal(30, reopened.AutoLockMinutes);
        // La copie garde l'ancien coffre : il s'ouvre toujours avec l'ancien mot de passe.
        var old = new SecretVault(kept!, new FakeProtector());
        Assert.True(old.Unlock("oublie-pour-de-bon"));
        Assert.Equal("mot-de-passe-du-serveur", old.Get("pwd:1"));
    }

    [Fact]
    public void A_secret_command_keeps_its_text_out_of_the_settings()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var command = new SavedTerminalCommand { Name = "mot de passe" };

        TerminalCommandSecrets.Store(command, "texte-secret-de-test", secret: true, vault);

        Assert.True(command.Secret);
        Assert.Equal(string.Empty, command.Command);
        Assert.Equal("texte-secret-de-test", TerminalCommandSecrets.Text(command, vault));
        var settings = new AppSettings();
        settings.Terminal.SavedCommands.Add(command);
        var file = Path.Combine(t.Path, "settings.json");
        new SettingsStore(file).Save(settings);
        Assert.DoesNotContain("texte-secret-de-test", File.ReadAllText(file));
        var reloaded = Assert.Single(new SettingsStore(file).Load().Terminal.SavedCommands);
        Assert.Equal("texte-secret-de-test", TerminalCommandSecrets.Text(reloaded, vault));
    }

    [Fact]
    public void A_command_that_is_no_longer_secret_leaves_the_vault()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var command = new SavedTerminalCommand { Name = "script" };
        TerminalCommandSecrets.Store(command, "secret", secret: true, vault);
        var id = TerminalCommandSecrets.VaultId(command);

        TerminalCommandSecrets.Store(command, "/opt/script.sh", secret: false, vault);

        Assert.False(command.Secret);
        Assert.Equal("/opt/script.sh", TerminalCommandSecrets.Text(command, vault));
        Assert.False(vault.Contains(id));
    }

    [Fact]
    public void A_locked_vault_leaves_the_command_as_it_was()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        vault.SetMasterPassword("un-maitre-de-test");
        vault.Lock();
        var command = new SavedTerminalCommand { Name = "commande", Command = "ls" };

        Assert.Throws<VaultLockedException>(() => TerminalCommandSecrets.Store(command, "secret", secret: true, vault));

        Assert.False(command.Secret);
        Assert.Equal("ls", command.Command);
    }

    [Fact]
    public void Deleting_a_secret_command_removes_its_secret()
    {
        using var t = new TempDir();
        var vault = NewVault(t);
        var command = new SavedTerminalCommand { Name = "commande" };
        TerminalCommandSecrets.Store(command, "secret", secret: true, vault);

        TerminalCommandSecrets.Forget(command, vault);

        Assert.False(vault.Contains(TerminalCommandSecrets.VaultId(command)));
    }

    // ---- Deux TwinPanel ouverts en même temps sur le même coffre ----

    [Fact]
    public void A_secret_saved_by_another_open_TwinPanel_is_not_erased()
    {
        using var t = new TempDir();
        var first = NewVault(t);
        var second = NewVault(t);
        first.Set("pwd:1", "un");
        second.Set("pwd:2", "deux"); // avant : écrasait « pwd:1 »
        Assert.Equal("un", second.Get("pwd:1"));
        Assert.Equal("deux", first.Get("pwd:2"));
        Assert.Equal("un", NewVault(t).Get("pwd:1"));
    }

    [Fact]
    public void A_master_password_changed_in_another_TwinPanel_locks_this_one_until_the_new_one_is_given()
    {
        using var t = new TempDir();
        var first = NewVault(t);
        first.Set("pwd:1", "secret");
        first.SetMasterPassword("ancien");
        var second = NewVault(t);
        Assert.True(second.Unlock("ancien"));

        first.SetMasterPassword("nouveau");

        Assert.False(second.IsUnlocked);
        Assert.Throws<VaultLockedException>(() => second.Get("pwd:1"));
        Assert.False(second.Unlock("ancien"));
        Assert.True(second.Unlock("nouveau"));
        Assert.Equal("secret", second.Get("pwd:1"));
        // Et l'ancienne image du coffre n'a pas été réécrite par-dessus.
        Assert.True(NewVault(t).Unlock("nouveau"));
    }

    [Fact]
    public void A_vault_reset_in_another_TwinPanel_is_seen_here()
    {
        using var t = new TempDir();
        var first = NewVault(t);
        first.Set("pwd:1", "secret");
        var second = NewVault(t);
        Assert.True(second.Contains("pwd:1"));
        first.ResetForgotten();
        Assert.False(second.Contains("pwd:1"));
        Assert.Null(second.Get("pwd:1"));
    }
}
