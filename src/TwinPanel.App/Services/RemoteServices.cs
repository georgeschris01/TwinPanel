// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using Avalonia.Threading;
using TwinPanel.Core;
using TwinPanel.Core.Archives;
using TwinPanel.Core.Remote;

namespace TwinPanel.App.Services;

/// <summary>Questions de connexion, avec des mots de passe déjà tapés (test de connexion, connexion rapide) donnés sans redemander.</summary>
public sealed class PrefilledPrompts(IConnectionPrompts inner, string? password, string? proxyPassword = null, string? certificatePassword = null) : IConnectionPrompts
{
    public Task<HostKeyDecision> ConfirmHostKeyAsync(HostKeyPrompt p) => inner.ConfirmHostKeyAsync(p);
    public Task<CertificateDecision> ConfirmCertificateAsync(CertificatePrompt p) => inner.ConfirmCertificateAsync(p);
    public Task<bool> UnlockVaultAsync(SecretVault vault) => inner.UnlockVaultAsync(vault);
    public Task<IReadOnlyList<string>?> AnswerInteractiveAsync(string t, string i, IReadOnlyList<InteractivePrompt> p) => inner.AnswerInteractiveAsync(t, i, p);

    public Task<SecretAnswer?> AskSecretAsync(SecretRequest r)
    {
        var typed = r.Title == L.T("Mot de passe") ? password
            : r.Title == "Proxy" ? proxyPassword
            : r.Title == L.T("Certificat client") ? certificatePassword
            : null;
        return string.IsNullOrEmpty(typed) ? inner.AskSecretAsync(r) : Task.FromResult<SecretAnswer?>(new SecretAnswer(typed, false));
    }
}

/// <summary>
/// Fichier ouvert depuis un serveur (F4, Entrée, double-clic) ou depuis une archive : il est copié dans un dossier
/// temporaire et surveillé ; à chaque enregistrement dans le programme, TwinPanel propose de le renvoyer sur le serveur
/// (comme Total Commander), ou automatiquement si l'utilisateur l'a choisi. Archive (lecture seule pour l'instant) :
/// proposition d'enregistrer la copie modifiée à côté de l'archive, pour ne pas perdre le travail.
/// </summary>
public sealed class RemoteEdits(IDialogService dialogs, Action<string> status, Func<Task> refresh) : IDisposable
{
    private readonly List<Entry> _entries = [];

    /// <summary>Options → Comportement : renvoyer sur le serveur sans demander.</summary>
    public bool AutoUpload { get; set; }

    /// <summary>Choix « Toujours renvoyer sans demander » fait dans la question : la fenêtre principale l'enregistre.</summary>
    public event EventHandler? AutoUploadChosen;

    private sealed class Entry : IDisposable
    {
        public RemoteSession? Session { get; init; }
        public required string Local { get; init; }
        /// <summary>Serveur : chemin distant ; archive : chemin de l'archive.</summary>
        public required string Target { get; init; }
        public bool IsArchive { get; init; }
        /// <summary>Dossier de l'archive d'où vient la copie.</summary>
        public string ArchiveFolder { get; init; } = string.Empty;
        public required FileSystemWatcher Watcher { get; init; }
        public required DispatcherTimer Debounce { get; init; }
        public DateTime LastUploaded { get; set; }
        public bool Asking { get; set; }
        public void Dispose()
        {
            Watcher.Dispose();
            Debounce.Stop();
        }
    }

    public void Watch(RemoteSession session, string localFile, string remotePath) => Add(session, localFile, remotePath, isArchive: false);

    /// <summary>Fichier extrait d'une archive pour être ouvert : surveillé pour ne pas perdre une modification.</summary>
    public void WatchArchiveCopy(string localFile, string archivePath, string innerKey) =>
        Add(null, localFile, archivePath, isArchive: true, ArchiveIndex.ParentOf(ArchivePath.NormalizeKey(innerKey)));

    private void Add(RemoteSession? session, string localFile, string target, bool isArchive, string archiveFolder = "")
    {
        // Même copie ouverte une deuxième fois : une seule surveillance (sinon la question viendrait deux fois).
        foreach (var old in _entries.Where(e => string.Equals(e.Local, localFile, PathText.Comparison)).ToList())
        {
            old.Dispose();
            _entries.Remove(old);
        }
        var dir = Path.GetDirectoryName(localFile)!;
        var watcher = new FileSystemWatcher(dir, Path.GetFileName(localFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        var entry = new Entry
        {
            Session = session,
            Local = localFile,
            Target = target,
            IsArchive = isArchive,
            ArchiveFolder = archiveFolder,
            Watcher = watcher,
            Debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) },
            LastUploaded = File.GetLastWriteTimeUtc(localFile),
        };
        entry.Debounce.Tick += async (_, _) => { entry.Debounce.Stop(); await OnChangedAsync(entry); };
        void Kick() => Dispatcher.UIThread.Post(() => { entry.Debounce.Stop(); entry.Debounce.Start(); });
        watcher.Changed += (_, _) => Kick();
        watcher.Created += (_, _) => Kick();
        watcher.Renamed += (_, _) => Kick(); // beaucoup de programmes enregistrent par « fichier temporaire puis renommage »
        watcher.EnableRaisingEvents = true;
        _entries.Add(entry);
    }

    private async Task OnChangedAsync(Entry e)
    {
        if (e.Asking || !File.Exists(e.Local)) return;
        var stamp = File.GetLastWriteTimeUtc(e.Local);
        if (stamp <= e.LastUploaded) return;
        e.Asking = true;
        var name = Path.GetFileName(e.Local);
        try
        {
            if (e.IsArchive) { await SaveArchiveCopyAsync(e, name, stamp); return; }
            if (e.Session is null) return;
            if (!AutoUpload)
            {
                var choice = await dialogs.ChooseAsync<int>(L.T("Fichier modifié"),
                    L.F("« {0} » a été modifié.\nLe renvoyer sur le serveur ({1} : {2}) ?", name, e.Session.Profile.Name, e.Target),
                    (L.T("Renvoyer"), 1), (L.T("Toujours renvoyer sans demander"), 2), (L.T("Ne pas renvoyer"), 0));
                if (choice is not (1 or 2))
                {
                    e.LastUploaded = stamp; // ne pas reposer la question pour cette version
                    status(L.F("« {0} » n'a pas été renvoyé (la copie modifiée reste ici : {1}).", name, e.Local));
                    return;
                }
                if (choice == 2)
                {
                    AutoUpload = true;
                    AutoUploadChosen?.Invoke(this, EventArgs.Empty);
                }
            }
            status(L.F("Envoi de {0}…", name));
            await Task.Run(() => RemoteTransfer.UploadReplaceAsync(e.Session, e.Local, e.Target, CancellationToken.None));
            e.LastUploaded = stamp;
            status(L.F("« {0} » renvoyé sur {1} ({2:HH:mm:ss}).", name, e.Session.Profile.Name, DateTime.Now));
            await refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TwinPanel.Core.FileOperationException)
        {
            await dialogs.ShowErrorAsync(e.IsArchive ? L.T("Fichier d'une archive") : L.T("Renvoyer sur le serveur"),
                ex.Message + L.T("\nLe fichier modifié reste ici : ") + e.Local);
        }
        finally { e.Asking = false; }
    }

    /// <summary>Archive modifiable : on propose de remplacer le fichier dedans. Sinon (rar, 7z chiffré…), de garder la copie à côté d'elle.</summary>
    private async Task SaveArchiveCopyAsync(Entry e, string name, DateTime stamp)
    {
        e.LastUploaded = stamp;
        var archiveName = Path.GetFileName(e.Target);
        var refusal = ArchiveEditor.CanEdit(e.Target) ? await Task.Run(() => ArchiveEditor.EncryptedRefusal(e.Target)) : L.T("format en lecture seule");
        if (refusal is null)
        {
            if (!await dialogs.ConfirmAsync(L.T("Fichier d'une archive modifié"), L.F("« {0} » a été modifié.\nRemplacer le fichier dans {1} ?", name, archiveName)))
            {
                status(L.F("Copie modifiée laissée ici : {0}", e.Local));
                return;
            }
            status(L.F("Mise à jour de {0}…", archiveName));
            await ArchiveEditor.ApplyAsync(e.Target, new ArchiveEdits { Add = [new ArchiveAddition(e.Local, e.ArchiveFolder)], Policy = TwinPanel.Core.ConflictPolicy.Overwrite });
            status(L.F("« {0} » remplacé dans {1}.", name, archiveName));
            await refresh();
            return;
        }

        var folder = Path.GetDirectoryName(e.Target) ?? Path.GetTempPath();
        var target = TwinPanel.Core.FileOperations.MakeUniqueName(Path.Combine(folder, name));
        if (!await dialogs.ConfirmAsync(L.T("Fichier d'une archive modifié"),
                L.F("« {0} » a été modifié, mais {1} ne peut pas être modifiée.\n", name, archiveName) +
                L.F("Enregistrer la copie modifiée à côté de l'archive ?\n{0}", target)))
        {
            status(L.F("Copie modifiée laissée ici : {0}", e.Local));
            return;
        }
        await Task.Run(() => File.Copy(e.Local, target, overwrite: false));
        status(L.F("« {0} » enregistré à côté de l'archive.", Path.GetFileName(target)));
        await refresh();
    }

    /// <summary>Arrête la surveillance des fichiers d'une connexion fermée.</summary>
    public void Forget(RemoteSession session)
    {
        foreach (var e in _entries.Where(e => e.Session == session).ToList()) { e.Dispose(); _entries.Remove(e); }
    }

    public void Dispose()
    {
        foreach (var e in _entries) e.Dispose();
        _entries.Clear();
    }
}
