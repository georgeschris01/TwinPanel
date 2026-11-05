// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.Services;

/// <summary>
/// Questions posées pendant une connexion (clé du serveur, certificat, mot de passe…).
/// Elles peuvent être demandées depuis un fil de travail : tout est ramené sur le fil de l'interface.
/// </summary>
public sealed class ConnectionPrompts : IConnectionPrompts
{
    /// <summary>Fenêtre active (le gestionnaire de connexions si ouvert, sinon la fenêtre principale).</summary>
    private static Window Owner()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.Windows.LastOrDefault(w => w.IsActive) ?? desktop.Windows.LastOrDefault(w => w.IsVisible) ?? desktop.MainWindow!;
        throw new InvalidOperationException(L.T("Pas de fenêtre."));
    }

    private static Task<T> OnUi<T>(Func<Task<T>> work) =>
        Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);

    // ---- Clé du serveur SSH ----------------------------------------------------------------------

    public Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt p) => OnUi(async () =>
    {
        var w = Dialogs.Create(p.Status == HostKeyStatus.Changed ? L.T("ALERTE : la clé du serveur a changé") : L.T("Nouveau serveur"));
        var result = HostKeyDecision.Reject;
        var where = p.Port == 22 ? p.Host : $"{p.Host}:{p.Port}";
        var fp = Dialogs.Mono($"{p.KeyType}\n{p.Fingerprint}");

        if (p.Status == HostKeyStatus.Changed)
        {
            var understood = new CheckBox { Content = L.T("Je sais pourquoi la clé a changé (serveur réinstallé, nouvelle clé…)") };
            var replace = new Button { Content = L.T("Remplacer la clé enregistrée"), IsEnabled = false };
            var refuse = new Button { Content = L.T("Refuser (recommandé)"), IsDefault = true, IsCancel = true, Classes = { "accent" } };
            understood.IsCheckedChanged += (_, _) => replace.IsEnabled = understood.IsChecked == true;
            replace.Click += (_, _) => { result = HostKeyDecision.AcceptAndSave; w.Close(); };
            refuse.Click += (_, _) => w.Close();
            w.Content = Dialogs.Stack(
                Dialogs.Text(L.F("La clé du serveur {0} n'est PLUS celle qui avait été acceptée.", where), bold: true, color: Brushes.IndianRed),
                Dialogs.Text(L.T("Quelqu'un pourrait se faire passer pour ton serveur (attaque « de l'homme du milieu »). ") +
                             L.T("Si tu n'as rien changé sur le serveur, refuse et vérifie d'abord.")),
                Dialogs.Text(L.T("Nouvelle empreinte :"), subtle: true), fp,
                understood,
                Dialogs.Buttons(refuse, replace));
            w.Opened += (_, _) => refuse.Focus();
        }
        else
        {
            var save = new Button { Content = L.T("Accepter et retenir"), IsDefault = true, Classes = { "accent" } };
            var once = new Button { Content = L.T("Accepter cette fois") };
            var refuse = new Button { Content = L.T("Refuser"), IsCancel = true };
            save.Click += (_, _) => { result = HostKeyDecision.AcceptAndSave; w.Close(); };
            once.Click += (_, _) => { result = HostKeyDecision.AcceptOnce; w.Close(); };
            refuse.Click += (_, _) => w.Close();
            w.Content = Dialogs.Stack(
                Dialogs.Text(L.F("Première connexion à {0}.", where), bold: true),
                Dialogs.Text(L.T("Voici l'empreinte de la clé du serveur. Pour être sûr que c'est bien lui, compare-la avec celle ") +
                             L.T("affichée sur le serveur par : ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub")),
                fp,
                Dialogs.Buttons(save, once, refuse));
            w.Opened += (_, _) => save.Focus();
        }
        await w.ShowDialog(Owner());
        return result;
    });

    // ---- Certificat TLS ---------------------------------------------------------------------------

    public Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt p) => OnUi(async () =>
    {
        var w = Dialogs.Create(L.T("Certificat non reconnu"));
        var result = CertificateDecision.Reject;
        var always = new Button { Content = L.T("Toujours faire confiance à ce certificat") };
        var once = new Button { Content = L.T("Accepter cette fois") };
        var refuse = new Button { Content = L.T("Refuser"), IsDefault = true, IsCancel = true, Classes = { "accent" } };
        always.Click += (_, _) => { result = CertificateDecision.AlwaysTrust; w.Close(); };
        once.Click += (_, _) => { result = CertificateDecision.AcceptOnce; w.Close(); };
        refuse.Click += (_, _) => w.Close();
        w.Content = Dialogs.Stack(
            Dialogs.Text(L.F("Le certificat de {0}:{1} n'est pas reconnu par le système.", p.Host, p.Port), bold: true),
            Dialogs.Text(string.Join(Environment.NewLine, p.Problems.Select(x => "• " + x)), color: Brushes.IndianRed),
            Dialogs.Mono(L.F("Sujet :     {0}\nÉmetteur :  {1}\nValide du {2:dd/MM/yyyy} au {3:dd/MM/yyyy}\nSHA-256 :\n{4}", p.Subject, p.Issuer, p.NotBefore, p.NotAfter, p.Sha256)),
            Dialogs.Text(L.T("Normal pour un serveur personnel avec un certificat auto-signé : vérifie l'empreinte SHA-256 avant d'accepter. ") +
                         L.T("« Toujours faire confiance » retient CE certificat précis : s'il change, la question reviendra."), subtle: true),
            Dialogs.Buttons(refuse, once, always));
        w.Opened += (_, _) => refuse.Focus();
        await w.ShowDialog(Owner());
        return result;
    });

    // ---- Mots de passe ------------------------------------------------------------------------------

    public Task<SecretAnswer?> AskSecretAsync(SecretRequest r) => OnUi(async () =>
    {
        var w = Dialogs.Create(r.Title);
        SecretAnswer? result = null;
        var box = Dialogs.Password();
        var remember = new CheckBox { Content = L.T("Enregistrer dans le coffre (chiffré)"), IsVisible = r.CanRemember };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { result = new SecretAnswer(box.Text ?? string.Empty, remember.IsChecked == true); w.Close(); };
        cancel.Click += (_, _) => w.Close();
        w.Content = Dialogs.Stack(Dialogs.Text(r.Message), box, Dialogs.ShowToggle(box), remember, Dialogs.Buttons(ok, cancel));
        w.Opened += (_, _) => box.Focus();
        await w.ShowDialog(Owner());
        return result;
    });

    public Task<bool> UnlockVaultAsync(SecretVault vault) => OnUi(async () =>
    {
        var w = Dialogs.Create(L.T("Coffre verrouillé"));
        bool ok = false;
        var box = Dialogs.Password();
        var error = Dialogs.Text(string.Empty, color: Brushes.IndianRed);
        var unlock = new Button { Content = L.T("Déverrouiller"), IsDefault = true, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true };
        unlock.Click += async (_, _) =>
        {
            unlock.IsEnabled = false;
            var pwd = box.Text ?? string.Empty;
            // PBKDF2 prend un moment : hors du fil de l'interface.
            bool good = await Task.Run(() => vault.Unlock(pwd));
            unlock.IsEnabled = true;
            if (good) { ok = true; w.Close(); }
            else { error.Text = L.T("Mot de passe maître incorrect."); box.SelectAll(); box.Focus(); }
        };
        cancel.Click += (_, _) => w.Close();
        w.Content = Dialogs.Stack(
            Dialogs.Text(L.T("Les mots de passe enregistrés sont protégés par ton mot de passe maître."), bold: true),
            box, Dialogs.ShowToggle(box), error, Dialogs.Buttons(unlock, cancel));
        w.Opened += (_, _) => box.Focus();
        await w.ShowDialog(Owner());
        return ok;
    });

    public Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string title, string instruction, IReadOnlyList<InteractivePrompt> prompts) => OnUi(async () =>
    {
        var w = Dialogs.Create(title);
        IReadOnlyList<string>? result = null;
        var boxes = prompts.Select(p => p.Echo ? new TextBox { MinWidth = 360 } : Dialogs.Password()).ToList();
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Classes = { "accent" } };
        var cancel = new Button { Content = L.T("Annuler"), IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => { result = boxes.Select(b => b.Text ?? string.Empty).ToList(); w.Close(); };
        cancel.Click += (_, _) => w.Close();
        var children = new List<Control>();
        if (!string.IsNullOrWhiteSpace(instruction)) children.Add(Dialogs.Text(instruction));
        for (int i = 0; i < prompts.Count; i++) { children.Add(Dialogs.Text(prompts[i].Text.Trim())); children.Add(boxes[i]); }
        children.Add(Dialogs.Buttons(ok, cancel));
        w.Content = Dialogs.Stack([.. children]);
        w.Opened += (_, _) => boxes.FirstOrDefault()?.Focus();
        await w.ShowDialog(Owner());
        return result;
    });
}

/// <summary>Petits éléments de dialogue construits en code.</summary>
internal static class Dialogs
{
    public static Window Create(string title)
    {
        var w = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        w.Bind(Window.BackgroundProperty, w.GetResourceObservable("AppBackground"));
        w.KeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        return w;
    }

    public static TextBlock Text(string text, bool bold = false, bool subtle = false, IBrush? color = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 };
        if (bold) t.FontWeight = FontWeight.SemiBold;
        if (subtle) t.Classes.Add("subtle");
        if (color is not null) t.Foreground = color;
        return t;
    }

    public static SelectableTextBlock Mono(string text) => new()
    {
        Text = text,
        FontFamily = new FontFamily("Consolas,Menlo,monospace"),
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 560,
    };

    public static TextBox Password() => new() { PasswordChar = '•', MinWidth = 360, RevealPassword = false };

    public static CheckBox ShowToggle(TextBox box)
    {
        var c = new CheckBox { Content = L.T("Afficher") };
        c.IsCheckedChanged += (_, _) => box.RevealPassword = c.IsChecked == true;
        return c;
    }

    public static Control Stack(params Control[] children)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12, MaxWidth = 600 };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    public static Control Buttons(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) panel.Children.Add(b);
        return panel;
    }
}
