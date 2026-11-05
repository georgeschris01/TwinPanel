// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.Services;

/// <summary>
/// Mot de passe maître : le changer, créer un code de secours, l'avoir oublié.
/// Partagé par le gestionnaire de connexions, les Options (Sécurité) et la fenêtre « Coffre verrouillé ».
/// </summary>
public sealed class VaultActions(SecretVault vault)
{
    public const int MinMasterLength = 8;

    private static DialogService Ui() => new(ConnectionPrompts.Owner());

    /// <summary>Définit ou change le mot de passe maître (l'actuel est demandé s'il y en a un), puis montre le nouveau code de secours.</summary>
    /// <returns>Message pour la barre d'état ; null si rien n'a changé.</returns>
    public async Task<string?> ChangeMasterAsync()
    {
        if (vault.HasMasterPassword && !vault.IsUnlocked && !await new ConnectionPrompts().UnlockVaultAsync(vault)) return null;
        if (await AskNewMasterAsync() is not { } password) return null;
        if (!await RunAsync(() => vault.SetMasterPassword(password))) return null;
        await CreateAndShowCodeAsync();
        return L.T("Mot de passe maître enregistré.");
    }

    /// <summary>Crée un code de secours (l'ancien ne marche plus) et le montre.</summary>
    public async Task<string?> CreateRecoveryCodeAsync()
    {
        if (!vault.HasMasterPassword)
        {
            await Ui().ShowErrorAsync(L.T("Code de secours"), L.T("Définis d'abord un mot de passe maître : le code de secours le remplace s'il est oublié."));
            return null;
        }
        if (!vault.IsUnlocked && !await new ConnectionPrompts().UnlockVaultAsync(vault)) return null;
        if (vault.HasRecoveryCode && !await Ui().ConfirmAsync(L.T("Code de secours"),
                L.T("Un nouveau code remplace l'ancien, qui ne marchera plus. Continuer ?"))) return null;
        return await CreateAndShowCodeAsync() ? L.T("Nouveau code de secours créé.") : null;
    }

    /// <summary>
    /// Mot de passe maître oublié : par le code de secours (rien n'est perdu), sinon en effaçant tous les secrets.
    /// Vrai si le coffre est utilisable ensuite.
    /// </summary>
    public async Task<bool> ForgotAsync()
    {
        if (!vault.HasMasterPassword) return true;
        var ui = Ui();
        var title = L.T("Mot de passe maître oublié");
        int? choice = vault.HasRecoveryCode
            ? await ui.ChooseAsync<int>(title,
                L.T("Avec ton code de secours, tu choisis un nouveau mot de passe maître sans rien perdre.\nSans lui, il faut effacer tous les secrets du coffre."),
                (L.T("J'ai mon code de secours"), 1), (L.T("Tout effacer et recommencer…"), 2), (L.T("Annuler"), null))
            : await ui.ChooseAsync<int>(title,
                L.T("Aucun code de secours n'a été créé pour ce coffre. Sans le mot de passe maître, personne ne peut relire les secrets : il faut les effacer."),
                (L.T("Tout effacer et recommencer…"), 2), (L.T("Annuler"), null));
        return choice switch
        {
            1 => await UseRecoveryCodeAsync(),
            2 => await ResetAsync(),
            _ => false,
        };
    }

    private async Task<bool> UseRecoveryCodeAsync()
    {
        var title = L.T("Code de secours");
        var answer = await new ConnectionPrompts().AskSecretAsync(new SecretRequest(title,
            L.T("Tape ton code de secours (les tirets, les espaces et les majuscules ne comptent pas) :"), false));
        if (answer is null) return false;
        if (!RecoveryCodes.IsWellFormed(answer.Value))
        {
            await Ui().ShowErrorAsync(title, L.F("Un code de secours a {0} caractères (lettres, chiffres et caractères spéciaux), sans compter les tirets. Vérifie ce que tu as tapé.", RecoveryCodes.Length));
            return false;
        }
        // Le calcul de la clé prend un moment : hors du fil de l'interface.
        if (!await Task.Run(() => vault.UnlockWithRecoveryCode(answer.Value)))
        {
            await Ui().ShowErrorAsync(title, L.T("Code de secours incorrect."));
            return false;
        }
        if (await AskNewMasterAsync(L.T("Coffre ouvert. Choisis ton nouveau mot de passe maître (au moins 8 caractères) :")) is not { } password)
        {
            await Ui().ShowErrorAsync(title, L.T("Le coffre est ouvert pour cette fois, mais l'ancien mot de passe maître reste en place. Change-le dès que possible."));
            return true;
        }
        if (!await RunAsync(() => vault.SetMasterPassword(password))) return true;
        // L'ancien code allait avec l'ancien mot de passe : un nouveau est créé tout de suite.
        await CreateAndShowCodeAsync();
        return true;
    }

    private async Task<bool> ResetAsync()
    {
        var title = L.T("Tout effacer et recommencer");
        if (!await Ui().ConfirmAsync(title,
                L.T("Tous les secrets du coffre vont être effacés : mots de passe des connexions, clés privées et leurs phrases de passe, commandes secrètes du terminal.\n\nLes fiches de connexion restent : les mots de passe seront redemandés, et les clés privées seront à réimporter.\n\nL'ancien coffre est d'abord copié à côté, au cas où le mot de passe te reviendrait.\n\nContinuer ?"))) return false;
        string? kept = null;
        if (!await RunAsync(() => kept = vault.ResetForgotten())) return false;
        var where = kept is null ? string.Empty : L.F("\n\nAncien coffre gardé dans :\n{0}", kept);
        if (!vault.CanWorkWithoutMaster)
        {
            // Sans trousseau, le coffre ne peut rien enregistrer sans mot de passe maître.
            await Ui().ShowErrorAsync(title, L.T("Coffre vidé. Sur ce système, il faut un mot de passe maître : choisis-en un nouveau.") + where);
            await ChangeMasterAsync();
            return true;
        }
        if (await Ui().ConfirmAsync(title, L.T("Coffre vidé. Choisir un nouveau mot de passe maître maintenant ?") + where))
            await ChangeMasterAsync();
        return true;
    }

    /// <summary>Nouveau mot de passe maître, tapé deux fois ; null si annulé ou refusé.</summary>
    private static async Task<string?> AskNewMasterAsync(string? message = null)
    {
        var prompts = new ConnectionPrompts();
        var title = L.T("Mot de passe maître");
        var first = await prompts.AskSecretAsync(new SecretRequest(title,
            message ?? L.T("Nouveau mot de passe maître (au moins 8 caractères). Un code de secours te sera donné ensuite, pour le cas où tu l'oublierais."), false));
        if (first is null) return null;
        if (first.Value.Length < MinMasterLength)
        {
            await Ui().ShowErrorAsync(title, L.T("Le mot de passe maître doit avoir au moins 8 caractères."));
            return null;
        }
        var second = await prompts.AskSecretAsync(new SecretRequest(title, L.T("Retape-le pour confirmer :"), false));
        if (second is null) return null;
        if (second.Value == first.Value) return first.Value;
        await Ui().ShowErrorAsync(title, L.T("Les deux mots de passe ne sont pas identiques."));
        return null;
    }

    private async Task<bool> CreateAndShowCodeAsync()
    {
        string? code = null;
        if (!await RunAsync(() => code = vault.CreateRecoveryCode()) || code is null) return false;
        await ShowCodeAsync(code);
        return true;
    }

    /// <summary>Travail du coffre hors du fil de l'interface ; une erreur est montrée.</summary>
    private static async Task<bool> RunAsync(Action work)
    {
        try
        {
            await Task.Run(work);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or VaultLockedException or VaultKeyringException or CryptographicException)
        {
            await Ui().ShowErrorAsync(L.T("Coffre des mots de passe"), ex.Message);
            return false;
        }
    }

    /// <summary>Le code n'est montré qu'ici, une seule fois : à noter, copier ou enregistrer dans un fichier.</summary>
    private async Task ShowCodeAsync(string code)
    {
        var owner = ConnectionPrompts.Owner();
        var w = Dialogs.Create(L.T("Code de secours"));
        var info = Dialogs.Text(string.Empty, subtle: true);
        var shown = Dialogs.Mono(code);
        shown.FontSize = 20;
        shown.FontWeight = FontWeight.SemiBold;
        var copy = new Button { Content = L.T("Copier") };
        var save = new Button { Content = L.T("Enregistrer…") };
        var done = new Button { Content = L.T("C'est noté"), IsDefault = true, Classes = { "accent" } };
        copy.Click += async (_, _) =>
        {
            if (w.Clipboard is { } c) await c.SetTextAsync(code);
            info.Text = L.T("Copié. Colle-le en lieu sûr, puis copie autre chose pour l'effacer du presse-papiers.");
        };
        save.Click += async (_, _) =>
        {
            var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.T("Enregistrer le code de secours"),
                SuggestedFileName = "twinpanel-code-de-secours.txt",
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            try
            {
                await File.WriteAllTextAsync(path, RecoveryFileText(code));
                info.Text = L.F("Enregistré dans {0}. Range ce fichier hors de ce PC (clé USB…).", path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { info.Text = ex.Message; }
        };
        done.Click += (_, _) => w.Close();
        w.Content = Dialogs.Stack(
            Dialogs.Text(L.T("Ton code de secours"), bold: true),
            shown,
            Dialogs.Text(L.T("Si tu oublies ton mot de passe maître, ce code permet d'en choisir un nouveau sans rien perdre. Note-le sur papier, ou enregistre-le hors de ce PC (clé USB…).")),
            Dialogs.Text((vault.UsesAccountProtection ? L.T("Il ne marche qu'avec ton compte, sur cette machine. ") : string.Empty) +
                         L.T("Il ne sera plus jamais affiché, et un nouveau code le remplace à chaque changement du mot de passe maître."), subtle: true),
            info,
            Dialogs.Buttons(copy, save, done));
        w.Opened += (_, _) => done.Focus();
        await w.ShowDialog(owner);
    }

    private static string RecoveryFileText(string code) =>
        L.T("TwinPanel : code de secours du coffre des mots de passe") + Environment.NewLine + Environment.NewLine +
        code + Environment.NewLine + Environment.NewLine +
        L.F("Créé le {0:dd/MM/yyyy HH:mm} sur {1}.", DateTime.Now, Environment.MachineName) + Environment.NewLine +
        L.T("Il remplace le mot de passe maître s'il est oublié (fenêtre « Coffre verrouillé » → « Mot de passe maître oublié ? »). Un nouveau code le remplace à chaque changement du mot de passe maître.") + Environment.NewLine;
}
