// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using TwinPanel.Core.Compare;

namespace TwinPanel.App.ViewModels;

/// <summary>Comparaison du contenu de deux fichiers : côte à côte pour du texte, octet par octet sinon.</summary>
public partial class CompareFilesViewModel(string left, string right) : ObservableObject
{
    private const long MaxTextSize = 20L * 1024 * 1024;

    public string LeftPath { get; } = left;
    public string RightPath { get; } = right;
    public ObservableCollection<DiffLine> Lines { get; } = [];

    [ObservableProperty] private string _summary = L.T("Comparaison…");
    [ObservableProperty] private bool _isText;

    public async Task LoadAsync()
    {
        try
        {
            bool binary = IsBinary(LeftPath) || IsBinary(RightPath)
                          || new FileInfo(LeftPath).Length > MaxTextSize || new FileInfo(RightPath).Length > MaxTextSize;
            if (binary)
            {
                var (same, offset) = await Task.Run(() => BinaryCompare(LeftPath, RightPath));
                Summary = same ? L.T("Les deux fichiers sont identiques (comparaison octet par octet).")
                    : L.F("Les fichiers sont différents (première différence à l'octet {0:N0}).", offset);
                return;
            }

            IsText = true;
            var diff = await Task.Run(() => TextDiff.Diff(ReadLines(LeftPath), ReadLines(RightPath)));
            foreach (var l in diff) Lines.Add(l);
            int changes = diff.Count(l => l.Kind != DiffKind.Same);
            Summary = changes == 0 ? L.T("Le texte des deux fichiers est identique.")
                : L.F("{0} ligne(s) différente(s) — vert : ajouté, rouge : supprimé, jaune : modifié.", changes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Summary = L.T("Erreur : ") + ex.Message;
        }
    }

    /// <summary>Un octet nul dans les 8 premiers Ko : on considère le fichier comme binaire.</summary>
    private static bool IsBinary(string path)
    {
        using var fs = File.OpenRead(path);
        var buffer = new byte[8192];
        int n = fs.Read(buffer, 0, buffer.Length);
        return Array.IndexOf(buffer, (byte)0, 0, n) >= 0;
    }

    private static (bool Same, long Offset) BinaryCompare(string a, string b)
    {
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        var ba = new byte[1024 * 1024];
        var bb = new byte[1024 * 1024];
        long position = 0;
        while (true)
        {
            int na = fa.ReadAtLeast(ba, ba.Length, throwOnEndOfStream: false);
            int nb = fb.ReadAtLeast(bb, bb.Length, throwOnEndOfStream: false);
            int common = Math.Min(na, nb);
            int diff = ba.AsSpan(0, common).CommonPrefixLength(bb.AsSpan(0, common));
            if (diff < common || na != nb) return (false, position + diff);
            if (na == 0) return (true, 0);
            position += na;
        }
    }

    private static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try { return Split(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes)); }
        catch (DecoderFallbackException) { return Split(Encoding.Latin1.GetString(bytes)); }
    }

    private static string[] Split(string text) => text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n');
}
