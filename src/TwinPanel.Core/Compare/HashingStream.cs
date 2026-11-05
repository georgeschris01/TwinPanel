// TwinPanel — © 2026 Georges Christophe (chrypox). Tous droits réservés.
// Licence TwinPanel 1.1 (gratuit, non commercial, sans modification) : voir le fichier LICENSE.
using System.Security.Cryptography;

namespace TwinPanel.Core.Compare;

/// <summary>Flux en écriture seule qui calcule l'empreinte de tout ce qu'on y écrit (fichier d'un serveur hashé pendant son téléchargement).</summary>
public sealed class HashingStream(IncrementalHash hash) : Stream
{
    private long _length;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _length;
    public override long Position { get => _length; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value)
    {
        // Reconnexion automatique : on repart de zéro.
        if (value != 0) throw new NotSupportedException();
        hash.GetHashAndReset();
        _length = 0;
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        hash.AppendData(buffer);
        _length += buffer.Length;
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        Write(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        Write(buffer.Span);
        return ValueTask.CompletedTask;
    }
    public string HexHash() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}
