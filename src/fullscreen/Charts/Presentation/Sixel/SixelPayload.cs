using System.Text;

namespace fullscreen.Charts;

/// <summary>Owned replay storage; never borrows the encoder's next-frame scratch buffer.</summary>
internal sealed class SixelPayload {
    private char[] _characters = [];
    internal int Length { get; private set; }
    internal ReadOnlySpan<char> Span => _characters.AsSpan(0, Length);

    internal void CopyFrom(StringBuilder output) {
        if (_characters.Length < output.Length) {
            Array.Resize(ref _characters, Math.Max(output.Length, Math.Max(256, _characters.Length * 2)));
        }
        output.CopyTo(0, _characters.AsSpan(), output.Length);
        Length = output.Length;
    }
}
