// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace TwinPanel.Core.Rights;

/// <summary>Une autorisation NTFS (une ligne de l'onglet Sécurité).</summary>
[SupportedOSPlatform("windows")]
public sealed record AclEntry(string Account, string Sid, bool Allow, FileSystemRights Rights,
    InheritanceFlags Inheritance, PropagationFlags Propagation, bool IsInherited)
{
    public string TypeLabel => Allow ? L.T("Autoriser") : L.T("Refuser");
    public string RightsLabel => NtfsRights.Label(Rights);
    public string AppliesToLabel => NtfsRights.AppliesTo(Inheritance, Propagation);
    public string InheritedLabel => IsInherited ? L.T("hérité") : string.Empty;
}

/// <summary>Propriétaire, héritage et autorisations d'un fichier ou dossier NTFS.</summary>
[SupportedOSPlatform("windows")]
public sealed record NtfsSecurity(string Owner, string OwnerSid, bool InheritanceEnabled, IReadOnlyList<AclEntry> Entries);

/// <summary>Niveaux d'autorisation proposés (comme l'onglet Sécurité de Windows) et « S'applique à ».</summary>
[SupportedOSPlatform("windows")]
public static class NtfsRights
{
    public sealed record Preset(string Label, FileSystemRights Rights)
    {
        public override string ToString() => Label;
    }

    // Bits « génériques » qu'on peut trouver dans les ACE héritables (CREATOR OWNER…).
    private const int GenericAll = 0x10000000, GenericExecute = 0x20000000, GenericWrite = 0x40000000, GenericRead = unchecked((int)0x80000000);

    public static IReadOnlyList<Preset> Presets { get; } =
    [
        new(L.T("Contrôle total"), FileSystemRights.FullControl),
        new(L.T("Modification"), FileSystemRights.Modify),
        new(L.T("Lecture et exécution"), FileSystemRights.ReadAndExecute),
        new(L.T("Lecture"), FileSystemRights.Read),
        new(L.T("Écriture"), FileSystemRights.Write),
        new(L.T("Lecture et écriture"), FileSystemRights.Read | FileSystemRights.Write),
    ];

    /// <summary>Les bits génériques convertis en droits de fichier, sans « Synchronize » (ajouté ou non selon Windows).</summary>
    public static FileSystemRights Normalize(FileSystemRights rights)
    {
        int r = (int)rights;
        if ((r & GenericAll) != 0) r |= (int)FileSystemRights.FullControl;
        if ((r & GenericRead) != 0) r |= (int)FileSystemRights.Read;
        if ((r & GenericWrite) != 0) r |= (int)FileSystemRights.Write;
        if ((r & GenericExecute) != 0) r |= (int)FileSystemRights.ExecuteFile;
        r &= (int)FileSystemRights.FullControl;
        return (FileSystemRights)r & ~FileSystemRights.Synchronize;
    }

    public static string Label(FileSystemRights rights)
    {
        var n = Normalize(rights);
        foreach (var p in Presets)
            if (Normalize(p.Rights) == n) return p.Label;
        return L.T("Autorisations spéciales");
    }

    public static Preset? PresetFor(FileSystemRights rights)
    {
        var n = Normalize(rights);
        return Presets.FirstOrDefault(p => Normalize(p.Rights) == n);
    }

    public sealed record Scope(string Label, InheritanceFlags Inheritance, PropagationFlags Propagation)
    {
        public override string ToString() => Label;
    }

    public static IReadOnlyList<Scope> FolderScopes { get; } =
    [
        new(L.T("Ce dossier, les sous-dossiers et les fichiers"), InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None),
        new(L.T("Ce dossier seulement"), InheritanceFlags.None, PropagationFlags.None),
        new(L.T("Ce dossier et les sous-dossiers"), InheritanceFlags.ContainerInherit, PropagationFlags.None),
        new(L.T("Ce dossier et les fichiers"), InheritanceFlags.ObjectInherit, PropagationFlags.None),
        new(L.T("Les sous-dossiers et les fichiers seulement"), InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly),
        new(L.T("Les sous-dossiers seulement"), InheritanceFlags.ContainerInherit, PropagationFlags.InheritOnly),
        new(L.T("Les fichiers seulement"), InheritanceFlags.ObjectInherit, PropagationFlags.InheritOnly),
    ];

    public static string AppliesTo(InheritanceFlags inheritance, PropagationFlags propagation)
    {
        var prop = propagation & PropagationFlags.InheritOnly;
        var s = FolderScopes.FirstOrDefault(x => x.Inheritance == inheritance && x.Propagation == prop);
        var label = s?.Label ?? (inheritance == InheritanceFlags.None ? L.T("Cet élément seulement") : L.T("Spécial"));
        if ((propagation & PropagationFlags.NoPropagateInherit) != 0) label += L.T(" (un seul niveau)");
        return label;
    }
}

/// <summary>
/// Sécurité NTFS : propriétaire, héritage, ajout / modification / suppression d'autorisations, attributs.
/// Écrit à partir des API .NET (System.Security.AccessControl) ; rien n'est repris d'un autre logiciel.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsSecurity
{
    /// <summary>Comptes proposés dans la liste (noms traduits par Windows : « Tout le monde », « Utilisateurs »…).</summary>
    public static IReadOnlyList<string> CommonAccounts()
    {
        var list = new List<string>();
        if (WindowsIdentity.GetCurrent().Name is { Length: > 0 } me) list.Add(me);
        foreach (var sid in new[]
                 {
                     WellKnownSidType.BuiltinUsersSid, WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid,
                     WellKnownSidType.AuthenticatedUserSid, WellKnownSidType.WorldSid, WellKnownSidType.CreatorOwnerSid,
                 })
        {
            try { list.Add(NameOf(new SecurityIdentifier(sid, null))); }
            catch (Exception ex) when (ex is ArgumentException or SystemException) { }
        }
        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static readonly Dictionary<string, string> NameCache = new(StringComparer.Ordinal);

    public static string NameOf(SecurityIdentifier sid)
    {
        lock (NameCache)
        {
            if (NameCache.TryGetValue(sid.Value, out var cached)) return cached;
            string name;
            try { name = sid.Translate(typeof(NTAccount)).Value; }
            catch (IdentityNotMappedException) { name = sid.Value; } // compte supprimé : on montre son identifiant
            catch (SystemException) { name = sid.Value; }
            NameCache[sid.Value] = name;
            return name;
        }
    }

    /// <summary>Propriétaire affiché dans la colonne des panneaux (« PC\Utilisateur ») ; null si illisible ou disque sans sécurité.</summary>
    public static string? TryOwnerName(string path)
    {
        try
        {
            var sec = Get(path, AccessControlSections.Owner);
            if (sec.GetOwner(typeof(SecurityIdentifier)) is not SecurityIdentifier sid) return null;
            var name = NameOf(sid);
            int slash = name.LastIndexOf('\\'); // « PC\Utilisateur » → « Utilisateur » (colonne étroite)
            return slash >= 0 && slash < name.Length - 1 ? name[(slash + 1)..] : name;
        }
        catch (SystemException)
        {
            return null; // illisible (droits, disque FAT…) : colonne vide
        }
    }

    /// <summary>« DOMAINE\nom », « nom », ou un SID « S-1-5-… ».</summary>
    public static SecurityIdentifier ResolveAccount(string account)
    {
        var a = account.Trim();
        if (a.Length == 0) throw new ArgumentException(L.T("Indique un compte (utilisateur ou groupe)."));
        if (a.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
        {
            try { return new SecurityIdentifier(a); }
            catch (ArgumentException) { /* pas un SID valable : on essaie comme nom */ }
        }
        try { return (SecurityIdentifier)new NTAccount(a).Translate(typeof(SecurityIdentifier)); }
        catch (IdentityNotMappedException ex) { throw new ArgumentException(L.F("Compte inconnu : « {0} ».", a), ex); }
    }

    private static FileSystemSecurity Get(string path, AccessControlSections sections) =>
        Directory.Exists(path) ? new DirectoryInfo(path).GetAccessControl(sections) : new FileInfo(path).GetAccessControl(sections);

    private static void Set(string path, FileSystemSecurity security)
    {
        if (security is DirectorySecurity d) new DirectoryInfo(path).SetAccessControl(d);
        else new FileInfo(path).SetAccessControl((FileSecurity)security);
    }

    public static NtfsSecurity Read(string path)
    {
        var sec = Get(path, AccessControlSections.Access | AccessControlSections.Owner);
        var owner = sec.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var entries = new List<AclEntry>();
        foreach (FileSystemAccessRule r in sec.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var sid = (SecurityIdentifier)r.IdentityReference;
            entries.Add(new AclEntry(NameOf(sid), sid.Value, r.AccessControlType == AccessControlType.Allow, r.FileSystemRights,
                r.InheritanceFlags, r.PropagationFlags, r.IsInherited));
        }
        return new NtfsSecurity(owner is null ? L.T("(inconnu)") : NameOf(owner), owner?.Value ?? string.Empty, !sec.AreAccessRulesProtected, entries);
    }

    /// <summary>Ajoute une autorisation (Windows la fusionne avec une autorisation existante du même compte et du même type).</summary>
    public static void AddRule(string path, string account, FileSystemRights rights, bool allow, InheritanceFlags inheritance, PropagationFlags propagation)
    {
        var sid = ResolveAccount(account);
        bool isDir = Directory.Exists(path);
        if (!isDir) { inheritance = InheritanceFlags.None; propagation = PropagationFlags.None; } // un fichier n'a pas d'enfants
        var sec = Get(path, AccessControlSections.Access);
        sec.AddAccessRule(new FileSystemAccessRule(sid, rights, inheritance, propagation, allow ? AccessControlType.Allow : AccessControlType.Deny));
        Set(path, sec);
    }

    /// <summary>Retire une autorisation propre à l'élément (une autorisation héritée se change sur le dossier parent).</summary>
    public static void RemoveRule(string path, AclEntry entry)
    {
        if (entry.IsInherited) throw new InvalidOperationException(L.T("Cette autorisation est héritée du dossier parent : change-la sur le parent, ou coupe l'héritage."));
        var sec = Get(path, AccessControlSections.Access);
        var sid = new SecurityIdentifier(entry.Sid);
        var type = entry.Allow ? AccessControlType.Allow : AccessControlType.Deny;
        bool removed = false;
        try
        {
            sec.RemoveAccessRuleSpecific(new FileSystemAccessRule(sid, entry.Rights, entry.Inheritance, entry.Propagation, type));
            removed = true;
        }
        catch (ArgumentException) { /* droits « génériques » non représentables : on retire toutes les autorisations de ce type pour ce compte */ }
        bool stillThere = sec.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r =>
            r.IdentityReference.Equals(sid) && r.AccessControlType == type && r.FileSystemRights == entry.Rights
            && r.InheritanceFlags == entry.Inheritance && r.PropagationFlags == entry.Propagation);
        if (!removed || stillThere)
            sec.RemoveAccessRuleAll(new FileSystemAccessRule(sid, FileSystemRights.Read, type));
        Set(path, sec);
    }

    /// <summary>Remplace une autorisation par une autre (même principe que « Modifier » dans Windows).</summary>
    public static void ReplaceRule(string path, AclEntry old, string account, FileSystemRights rights, bool allow, InheritanceFlags inheritance, PropagationFlags propagation)
    {
        RemoveRule(path, old);
        AddRule(path, account, rights, allow, inheritance, propagation);
    }

    /// <summary>Active ou coupe l'héritage. En le coupant, keepCopies garde les autorisations héritées comme autorisations propres.</summary>
    public static void SetInheritance(string path, bool enabled, bool keepCopies)
    {
        var sec = Get(path, AccessControlSections.Access);
        sec.SetAccessRuleProtection(!enabled, keepCopies);
        Set(path, sec);
    }

    /// <summary>Change le propriétaire (avec les privilèges d'administrateur s'ils sont disponibles).</summary>
    public static void SetOwner(string path, SecurityIdentifier owner)
    {
        Privileges.EnableOwnership();
        var sec = Get(path, AccessControlSections.Owner);
        sec.SetOwner(owner);
        try { Set(path, sec); }
        catch (UnauthorizedAccessException ex) when (!Privileges.IsElevated)
        {
            throw new UnauthorizedAccessException(L.T("Windows refuse : pour donner un fichier à un autre compte, lance TwinPanel en administrateur."), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new UnauthorizedAccessException(L.T("Windows refuse ce propriétaire : ") + ex.Message, ex);
        }
    }

    /// <summary>Attributs (lecture seule, caché, système, archive) : bits à mettre et à enlever.</summary>
    public static void ChangeAttributes(string path, FileAttributes set, FileAttributes clear)
    {
        var current = File.GetAttributes(path);
        var updated = (current & ~clear) | set;
        if (updated != current) File.SetAttributes(path, updated & ~FileAttributes.Directory | (current & FileAttributes.Directory));
    }

    /// <summary>Droits du processus (administrateur) et activation des privilèges « prendre possession » / « restaurer ».</summary>
    private static class Privileges
    {
        private static bool _tried;

        public static bool IsElevated
        {
            get
            {
                using var id = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static void EnableOwnership()
        {
            if (_tried) return;
            _tried = true;
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token)) return;
            try
            {
                foreach (var name in new[] { "SeTakeOwnershipPrivilege", "SeRestorePrivilege" })
                {
                    if (!LookupPrivilegeValue(null, name, out var luid)) continue;
                    var tp = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                    // Échoue silencieusement si le compte n'a pas ce privilège (pas administrateur) : Windows refusera ensuite.
                    AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf<TokenPrivileges>(), IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Win32Exception) { }
            finally { CloseHandle(token); }
        }

        private const uint TokenAdjustPrivileges = 0x0020, TokenQuery = 0x0008;
        private const int SePrivilegeEnabled = 0x00000002;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct TokenPrivileges
        {
            public int Count;
            public long Luid;
            public int Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? systemName, string name, out long luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAll, ref TokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}

/// <summary>Un élément local et tout son contenu (sans suivre les liens / jonctions), pour les changements « récursifs ».</summary>
public static class LocalTree
{
    public static IEnumerable<string> Enumerate(string path, bool recursive)
    {
        yield return path;
        if (!recursive || !Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) yield break;
        // Parcours à la main pour ne jamais entrer dans un lien ou une jonction (boucles, autre disque).
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<FileSystemInfo> children;
            try { children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 }).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var c in children)
            {
                yield return c.FullName;
                if (c is DirectoryInfo && (c.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(c.FullName);
            }
        }
    }

    /// <summary>Taille totale, nombre de fichiers et de dossiers (contenu illisible ignoré).</summary>
    public static (long Bytes, int Files, int Folders) Measure(string path, CancellationToken ct)
    {
        long bytes = 0; int files = 0, folders = 0;
        foreach (var p in Enumerate(path, true).Skip(Directory.Exists(path) ? 1 : 0))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(p);
                if ((info.Attributes & FileAttributes.Directory) != 0) folders++;
                else { files++; bytes += info.Length; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        if (!Directory.Exists(path)) { files = 1; bytes = new FileInfo(path).Length; }
        return (bytes, files, folders);
    }
}
