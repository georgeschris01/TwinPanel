// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Globalization;
using System.Text;

namespace TwinPanel.Core.Terminal;

public enum TerminalKey
{
    Up, Down, Right, Left, Home, End, Insert, Delete, PageUp, PageDown,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Enter, Tab, Backspace, Escape,
}

[Flags]
public enum TerminalModifiers { None = 0, Shift = 1, Alt = 2, Control = 4 }

public enum TerminalMouseButton { Left = 0, Middle = 1, Right = 2, None = 3, WheelUp = 64, WheelDown = 65 }

public enum TerminalMouseAction { Press, Release, Move }

/// <summary>Traduit le clavier et la souris en suites envoyées au programme.</summary>
public static class TerminalKeys
{
    /// <summary>Suite d'une touche spéciale (null : touche sans suite).</summary>
    public static string? Encode(TerminalKey key, TerminalModifiers modifiers, bool applicationCursorKeys)
    {
        int code = modifiers == TerminalModifiers.None ? 0
            : 1 + (modifiers.HasFlag(TerminalModifiers.Shift) ? 1 : 0)
                + (modifiers.HasFlag(TerminalModifiers.Alt) ? 2 : 0)
                + (modifiers.HasFlag(TerminalModifiers.Control) ? 4 : 0);
        bool alt = modifiers.HasFlag(TerminalModifiers.Alt);

        switch (key)
        {
            case TerminalKey.Enter: return alt ? "\u001b\r" : "\r";
            case TerminalKey.Tab: return modifiers.HasFlag(TerminalModifiers.Shift) ? "\u001b[Z" : "\t";
            case TerminalKey.Backspace:
                if (modifiers.HasFlag(TerminalModifiers.Control)) return alt ? "\u001b\b" : "\b";
                return alt ? "\u001b\u007f" : "\u007f";
            case TerminalKey.Escape: return "\u001b";
            case TerminalKey.Up: return Arrow('A');
            case TerminalKey.Down: return Arrow('B');
            case TerminalKey.Right: return Arrow('C');
            case TerminalKey.Left: return Arrow('D');
            case TerminalKey.Home: return Arrow('H');
            case TerminalKey.End: return Arrow('F');
            case TerminalKey.Insert: return Tilde(2);
            case TerminalKey.Delete: return Tilde(3);
            case TerminalKey.PageUp: return Tilde(5);
            case TerminalKey.PageDown: return Tilde(6);
            case TerminalKey.F1: return Function('P');
            case TerminalKey.F2: return Function('Q');
            case TerminalKey.F3: return Function('R');
            case TerminalKey.F4: return Function('S');
            case TerminalKey.F5: return Tilde(15);
            case TerminalKey.F6: return Tilde(17);
            case TerminalKey.F7: return Tilde(18);
            case TerminalKey.F8: return Tilde(19);
            case TerminalKey.F9: return Tilde(20);
            case TerminalKey.F10: return Tilde(21);
            case TerminalKey.F11: return Tilde(23);
            case TerminalKey.F12: return Tilde(24);
            default: return null;
        }

        string Arrow(char final) => code != 0 ? $"\u001b[1;{N(code)}{final}"
            : applicationCursorKeys ? $"\u001bO{final}" : $"\u001b[{final}";
        string Function(char final) => code != 0 ? $"\u001b[1;{N(code)}{final}" : $"\u001bO{final}";
        string Tilde(int number) => code != 0 ? $"\u001b[{N(number)};{N(code)}~" : $"\u001b[{N(number)}~";
    }

    /// <summary>Ctrl + caractère : les lettres et quelques signes donnent un caractère de commande.</summary>
    public static string? ControlCharacter(char c)
    {
        c = char.ToUpperInvariant(c);
        return c switch
        {
            >= 'A' and <= 'Z' => ((char)(c - 'A' + 1)).ToString(),
            ' ' or '2' or '@' => "\0",
            '[' or '3' => "\u001b",
            '\\' or '4' => "\u001c",
            ']' or '5' => "\u001d",
            '^' or '6' => "\u001e",
            '_' or '-' or '7' => "\u001f",
            '8' or '?' => "\u007f",
            _ => null,
        };
    }

    /// <summary>
    /// Texte collé : les retours à la ligne deviennent des Entrée, et le collage est encadré si le programme le demande.
    /// Un texte piégé ne doit rien pouvoir lancer : sans caractère de contrôle, il ne peut ni fermer le cadre ni agir
    /// sur la ligne en cours, et un dernier retour à la ligne ne valide pas la commande.
    /// </summary>
    public static string Paste(string text, bool bracketed)
    {
        text = text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
        text = string.Concat(text.Where(c => c is '\t' or '\r' || !char.IsControl(c)));
        return bracketed ? "\u001b[200~" + text + "\u001b[201~" : text.TrimEnd('\r');
    }

    /// <summary>Le texte contient-il plusieurs lignes, donc risque-t-il de lancer des commandes dès le collage ?</summary>
    public static bool IsMultiline(string text) => text.TrimEnd('\r', '\n').IndexOfAny(['\r', '\n']) >= 0;

    /// <summary>Souris envoyée au programme (null : ce geste n'est pas demandé par le programme, ou la case est trop loin).</summary>
    public static string? Mouse(TerminalMouseButton button, TerminalMouseAction action, int column, int row, TerminalModifiers modifiers,
        MouseTracking tracking, bool sgr)
    {
        if (tracking == MouseTracking.None) return null;
        if (action == TerminalMouseAction.Move && tracking == MouseTracking.Press) return null;
        if (action == TerminalMouseAction.Move && tracking == MouseTracking.Drag && button == TerminalMouseButton.None) return null;

        int code = (int)button
            + (modifiers.HasFlag(TerminalModifiers.Shift) ? 4 : 0)
            + (modifiers.HasFlag(TerminalModifiers.Alt) ? 8 : 0)
            + (modifiers.HasFlag(TerminalModifiers.Control) ? 16 : 0)
            + (action == TerminalMouseAction.Move ? 32 : 0);

        if (sgr) return $"\u001b[<{N(code)};{N(column + 1)};{N(row + 1)}{(action == TerminalMouseAction.Release ? 'm' : 'M')}";

        // Ancienne écriture : pas de bouton au relâchement, et 223 cases au plus.
        if (action == TerminalMouseAction.Release) code = (code & ~3) | 3;
        if (column > 222 || row > 222) return null;
        return new StringBuilder("\u001b[M").Append((char)(code + 32)).Append((char)(column + 33)).Append((char)(row + 33)).ToString();
    }

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}
