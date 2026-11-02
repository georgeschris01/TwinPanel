// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.0 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Text;

namespace TwinPanel.Core.Terminal;

/// <summary>Ce que le flux d'un programme demande au terminal, une fois découpé.</summary>
public interface IVtHandler
{
    /// <summary>Un caractère à afficher.</summary>
    void Print(Rune rune);

    /// <summary>Caractère de commande (retour chariot, saut de ligne…).</summary>
    void Control(byte code);

    /// <summary>Suite courte : ESC, un éventuel caractère intermédiaire, un caractère final.</summary>
    void Escape(char intermediate, char final);

    /// <summary>Suite CSI. <paramref name="parameters"/> est le texte brut des paramètres (« 1;31 », « 38:2::1:2:3 »).</summary>
    void Csi(char prefix, char intermediate, char final, string parameters);

    /// <summary>Suite OSC : titre de la fenêtre, etc.</summary>
    void Osc(string text);
}

/// <summary>
/// Découpe le flux d'octets d'un programme : texte UTF-8 et suites de commandes (normes ECMA-48 et xterm).
/// Une suite peut arriver en plusieurs morceaux : l'état est gardé d'un appel à l'autre.
/// </summary>
public sealed class VtParser(IVtHandler handler)
{
    private enum State { Ground, Escape, Csi, CsiIgnore, Osc, OscEscape, Skip, SkipEscape }

    /// <summary>Longueur maximale gardée d'une suite : au-delà, c'est un flux abîmé ou hostile.</summary>
    private const int MaxSequence = 4096;

    private State _state;
    private readonly StringBuilder _sequence = new();
    private char _prefix;
    private char _intermediate;

    // UTF-8 en cours : le caractère peut être coupé entre deux morceaux.
    private int _utf8Value;
    private int _utf8Missing;
    private int _utf8Minimum;

    public void Feed(ReadOnlySpan<byte> data)
    {
        foreach (var b in data) Next(b);
    }

    private void Next(byte b)
    {
        if (_state == State.Ground && _utf8Missing > 0)
        {
            if ((b & 0xC0) == 0x80)
            {
                _utf8Value = (_utf8Value << 6) | (b & 0x3F);
                if (--_utf8Missing == 0) PrintDecoded();
                return;
            }
            // Suite coupée : on signale le caractère abîmé puis on traite cet octet normalement.
            _utf8Missing = 0;
            handler.Print(Rune.ReplacementChar);
        }

        // Annulent toute suite en cours.
        if (b is 0x18 or 0x1A)
        {
            _state = State.Ground;
            handler.Control(b);
            return;
        }

        switch (_state)
        {
            case State.Ground: Ground(b); break;
            case State.Escape: Escape(b); break;
            case State.Csi: Csi(b); break;
            case State.CsiIgnore: if (b is >= 0x40 and <= 0x7E) _state = State.Ground; break;
            case State.Osc: Osc(b); break;
            case State.OscEscape:
                _state = State.Ground;
                handler.Osc(_sequence.ToString());
                if (b != (byte)'\\') Escape(b);
                break;
            case State.Skip: if (b == 0x1B) _state = State.SkipEscape; else if (b == 0x07) _state = State.Ground; break;
            case State.SkipEscape: _state = b == (byte)'\\' ? State.Ground : State.Skip; break;
        }
    }

    private void Ground(byte b)
    {
        if (b == 0x1B) { BeginEscape(); return; }
        if (b < 0x20 || b == 0x7F) { if (b != 0x7F) handler.Control(b); return; }
        if (b < 0x80) { handler.Print(new Rune(b)); return; }

        if ((b & 0xE0) == 0xC0) StartUtf8(b & 0x1F, 1, 0x80);
        else if ((b & 0xF0) == 0xE0) StartUtf8(b & 0x0F, 2, 0x800);
        else if ((b & 0xF8) == 0xF0) StartUtf8(b & 0x07, 3, 0x10000);
        else handler.Print(Rune.ReplacementChar);
    }

    private void StartUtf8(int value, int missing, int minimum)
    {
        _utf8Value = value;
        _utf8Missing = missing;
        _utf8Minimum = minimum;
    }

    private void PrintDecoded() =>
        handler.Print(_utf8Value >= _utf8Minimum && Rune.IsValid(_utf8Value) ? new Rune(_utf8Value) : Rune.ReplacementChar);

    private void BeginEscape()
    {
        _state = State.Escape;
        _intermediate = '\0';
    }

    private void Escape(byte b)
    {
        if (b < 0x20) { if (b == 0x1B) BeginEscape(); else handler.Control(b); return; }
        if (b is >= 0x20 and <= 0x2F) { _intermediate = (char)b; return; }

        _state = State.Ground;
        if (_intermediate != '\0') { handler.Escape(_intermediate, (char)b); return; }
        switch ((char)b)
        {
            case '[':
                _state = State.Csi;
                _sequence.Clear();
                _prefix = '\0';
                _intermediate = '\0';
                break;
            case ']':
                _state = State.Osc;
                _sequence.Clear();
                break;
            case 'P' or 'X' or '^' or '_':
                // DCS, SOS, PM, APC : rien d'utile ici, on les passe sans les afficher.
                _state = State.Skip;
                break;
            default:
                handler.Escape('\0', (char)b);
                break;
        }
    }

    private void Csi(byte b)
    {
        if (b < 0x20) { if (b == 0x1B) BeginEscape(); else handler.Control(b); return; }
        var c = (char)b;
        if (c is >= '<' and <= '?' && _sequence.Length == 0 && _prefix == '\0') { _prefix = c; return; }
        if (c is (>= '0' and <= '9') or ';' or ':')
        {
            if (_sequence.Length >= MaxSequence) { _state = State.CsiIgnore; return; }
            _sequence.Append(c);
            return;
        }
        if (c is >= ' ' and <= '/') { _intermediate = c; return; }
        if (c is >= '@' and <= '~')
        {
            _state = State.Ground;
            handler.Csi(_prefix, _intermediate, c, _sequence.ToString());
            return;
        }
        _state = State.CsiIgnore;
    }

    private void Osc(byte b)
    {
        if (b == 0x07) { _state = State.Ground; handler.Osc(_sequence.ToString()); return; }
        if (b == 0x1B) { _state = State.OscEscape; return; }
        if (b < 0x20) return;
        if (_sequence.Length < MaxSequence) _sequence.Append((char)b);
    }
}
