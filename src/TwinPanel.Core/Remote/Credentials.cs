// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
namespace TwinPanel.Core.Remote;

/// <summary>
/// Identifiants prêts à l'emploi pour une connexion (gardés en mémoire seulement le temps de la session,
/// pour ouvrir d'autres connexions sans redemander). Les secrets demandés à l'utilisateur avec « Enregistrer »
/// ne sont mis dans le coffre qu'APRÈS une connexion réussie (<see cref="CommitRemembered"/>).
/// </summary>
public sealed class ResolvedCredentials
{
    public string UserName { get; init; } = string.Empty;
    public string? Password { get; set; }
    public string? KeyText { get; init; }
    public string? KeyPassphrase { get; set; }
    public string? KeyName { get; init; }
    public string? ProxyPassword { get; set; }
    public string? CertificatePassword { get; set; }

    internal List<Action> PendingSaves { get; } = [];

    /// <summary>Enregistre dans le coffre les secrets que l'utilisateur a demandé de retenir.</summary>
    public void CommitRemembered()
    {
        foreach (var save in PendingSaves) save();
        PendingSaves.Clear();
    }
}

/// <summary>Rassemble identifiant, mot de passe, clé… depuis la fiche, l'identité, le coffre ou en les demandant.</summary>
public static class CredentialResolver
{
    public static async Task<ResolvedCredentials?> ResolveAsync(ConnectionProfile profile, ConnectionStore? store, IConnectionPrompts prompts)
    {
        var identity = store?.FindIdentity(profile.IdentityId);
        var keyId = identity?.KeyId ?? profile.KeyId;
        var key = store?.FindKey(keyId);
        bool saved = store?.FindProfile(profile.Id) is not null; // une connexion rapide non enregistrée n'a pas de coffre
        var user = identity?.UserName is { Length: > 0 } u ? u : profile.UserName;
        if (profile.Auth == AuthMode.Anonymous) user = "anonymous";

        if (store is not null && saved && !await EnsureUnlockedAsync(store, prompts, profile, identity, key)) return null;

        string? keyText = null;
        if (profile.Auth is AuthMode.Key or AuthMode.KeyAndPassword)
        {
            if (key is null && profile.MissingKey.Length > 0) throw new MissingKeyException(profile);
            if (key is null || store is null) throw new RemoteException(L.T("La clé choisie pour cette connexion n'existe plus dans le trousseau."));
            keyText = store.Vault.Get(SecretIds.KeyMaterial(key.Id))
                      ?? throw new RemoteException(L.F("La clé « {0} » est introuvable dans le coffre.", key.Name));
        }

        var creds = new ResolvedCredentials { UserName = user, KeyText = keyText, KeyName = key?.Name };
        var who = $"{user}@{profile.Host}";

        // Phrase de passe de la clé
        if (key is not null && keyText is not null && key.HasPassphrase)
        {
            creds.KeyPassphrase = key.SavePassphrase ? store!.Vault.Get(SecretIds.KeyPassphrase(key.Id)) : null;
            if (creds.KeyPassphrase is null)
            {
                var a = await prompts.AskSecretAsync(new SecretRequest(L.T("Phrase de passe"), L.F("Phrase de passe de la clé « {0} » ({1}) :", key.Name, who), CanRemember: true));
                if (a is null) return null;
                creds.KeyPassphrase = a.Value;
                if (a.Remember) creds.PendingSaves.Add(() =>
                {
                    store!.Vault.Set(SecretIds.KeyPassphrase(key.Id), a.Value);
                    key.SavePassphrase = true;
                    store.SaveKey(key);
                });
            }
        }

        // Mot de passe
        if (profile.Auth is AuthMode.Password or AuthMode.KeyAndPassword)
        {
            if (identity is not null)
            {
                creds.Password = identity.SavePassword ? store!.Vault.Get(SecretIds.IdentityPassword(identity.Id)) : null;
                if (creds.Password is null)
                {
                    var a = await prompts.AskSecretAsync(new SecretRequest(L.T("Mot de passe"), L.F("Mot de passe de l'identité « {0} » ({1}) :", identity.Name, who), CanRemember: true));
                    if (a is null) return null;
                    creds.Password = a.Value;
                    if (a.Remember) creds.PendingSaves.Add(() =>
                    {
                        store!.Vault.Set(SecretIds.IdentityPassword(identity.Id), a.Value);
                        identity.SavePassword = true;
                        store.SaveIdentity(identity);
                    });
                }
            }
            else
            {
                creds.Password = saved && profile.SavePassword ? store!.Vault.Get(SecretIds.Password(profile.Id)) : null;
                if (creds.Password is null)
                {
                    var a = await prompts.AskSecretAsync(new SecretRequest(L.T("Mot de passe"), L.F("Mot de passe pour {0} :", who), CanRemember: saved));
                    if (a is null) return null;
                    creds.Password = a.Value;
                    if (a.Remember && saved) creds.PendingSaves.Add(() =>
                    {
                        store!.Vault.Set(SecretIds.Password(profile.Id), a.Value);
                        profile.SavePassword = true;
                        store.SaveProfile(profile);
                    });
                }
            }
        }
        else if (profile.Auth == AuthMode.Anonymous) creds.Password = "anonymous@";

        // Proxy avec identifiant
        if (profile.Proxy != ProxyKind.None && profile.ProxyUser.Length > 0)
        {
            creds.ProxyPassword = saved && profile.SaveProxyPassword ? store!.Vault.Get(SecretIds.ProxyPassword(profile.Id)) : null;
            if (creds.ProxyPassword is null)
            {
                var a = await prompts.AskSecretAsync(new SecretRequest("Proxy", L.F("Mot de passe du proxy pour {0}@{1} :", profile.ProxyUser, profile.ProxyHost), CanRemember: saved));
                if (a is null) return null;
                creds.ProxyPassword = a.Value;
                if (a.Remember && saved) creds.PendingSaves.Add(() =>
                {
                    store!.Vault.Set(SecretIds.ProxyPassword(profile.Id), a.Value);
                    profile.SaveProxyPassword = true;
                    store.SaveProfile(profile);
                });
            }
        }

        // Certificat client protégé par mot de passe
        if (profile.ClientCertificatePath.Length > 0)
        {
            creds.CertificatePassword = saved && profile.SaveClientCertificatePassword ? store!.Vault.Get(SecretIds.CertificatePassword(profile.Id)) : null;
            if (creds.CertificatePassword is null)
            {
                var a = await prompts.AskSecretAsync(new SecretRequest(L.T("Certificat client"),
                    L.F("Mot de passe du certificat {0} (laisser vide s'il n'en a pas) :", Path.GetFileName(profile.ClientCertificatePath)), CanRemember: saved));
                if (a is null) return null;
                creds.CertificatePassword = a.Value;
                if (a.Remember && saved && a.Value.Length > 0) creds.PendingSaves.Add(() =>
                {
                    store!.Vault.Set(SecretIds.CertificatePassword(profile.Id), a.Value);
                    profile.SaveClientCertificatePassword = true;
                    store.SaveProfile(profile);
                });
            }
        }
        return creds;
    }

    /// <summary>Le coffre n'est déverrouillé que s'il faut vraiment y lire quelque chose.</summary>
    private static async Task<bool> EnsureUnlockedAsync(ConnectionStore store, IConnectionPrompts prompts,
                                                       ConnectionProfile profile, KeychainIdentity? identity, KeychainKey? key)
    {
        bool needs = profile.SavePassword || profile.SaveProxyPassword || profile.SaveClientCertificatePassword
                     || identity?.SavePassword == true || key is not null;
        if (!needs || store.Vault.IsUnlocked) return true;
        return await prompts.UnlockVaultAsync(store.Vault);
    }
}

/// <summary>Connexion restaurée d'une sauvegarde sans sa clé : rien n'est tenté, le message dit quoi faire.</summary>
public sealed class MissingKeyException(ConnectionProfile profile) : IOException(
    L.F("La connexion « {0} » utilise une clé qui n'est pas sur cet ordinateur : {1}.", profile.Name, profile.MissingKey) + "\n\n" +
    L.T("Ajoutez-la dans le Gestionnaire de connexions (onglet Clés), puis choisissez-la dans cette connexion. Pour tout transporter d'un ordinateur à l'autre, utilisez Options → Synchronisation."))
{
    public ConnectionProfile Profile { get; } = profile;
}
