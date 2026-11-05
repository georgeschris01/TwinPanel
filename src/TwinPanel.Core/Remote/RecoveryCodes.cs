// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;
using System.Text;

namespace TwinPanel.Core.Remote;

/// <summary>
/// Codes de secours du coffre : 24 caractères tirés au hasard (lettres, chiffres et caractères spéciaux, environ 128 bits),
/// par groupes de 4 (« K7Q#-M2P%-… »). Sans 0 / O, 1 / I / L : faciles à recopier depuis un papier.
/// Le tiret ne sert qu'à séparer les groupes ; les caractères spéciaux se tapent sur tous les claviers courants.
/// </summary>
public static class RecoveryCodes
{
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789!#$%&*+=?@";
    public const int Length = 24;

    public static string New()
    {
        var sb = new StringBuilder(Length + Length / 4);
        for (int i = 0; i < Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>Tel qu'il sert à la clé : majuscules, sans tirets ni espaces (le code se tape comme on veut).</summary>
    public static string Normalize(string code)
    {
        var sb = new StringBuilder(code.Length);
        foreach (var ch in code)
            if (ch != '-' && !char.IsWhiteSpace(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    /// <summary>A la bonne forme (longueur et caractères) : une faute de frappe se voit avant le long calcul de la clé.</summary>
    public static bool IsWellFormed(string code)
    {
        var n = Normalize(code);
        return n.Length == Length && n.All(c => Alphabet.Contains(c));
    }
}
