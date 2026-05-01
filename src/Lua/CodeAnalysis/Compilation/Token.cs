using System.Diagnostics;

namespace Lua.CodeAnalysis.Compilation;

[DebuggerDisplay("{DebuggerDisplay}")]
readonly struct Token(int pos, int t, int rawLength = 0)
{
    public Token(int pos, int t, string str) : this(pos, t)
    {
        S = str;
        N = 0;
    }

    public Token(int pos, int t, string str, int rawLength) : this(pos, t, rawLength)
    {
        S = str;
        N = 0;
    }

    public Token(int pos, double n, int rawLength) : this(pos, Scanner.TkNumber, rawLength)
    {
        N = n;
        S = string.Empty;
        IsInteger = false;
    }

    public Token(int pos, long i, int rawLength, bool isInteger) : this(pos, Scanner.TkNumber, rawLength)
    {
        // Reinterpret long bits into the N (double) field so existing call sites still see
        // a reasonable double via the integer's converted representation.
        N = (double)i;
        I = i;
        S = string.Empty;
        IsInteger = isInteger;
    }

    public readonly int Pos = pos;
    public readonly int T = t;
    public readonly int RawLength = rawLength;
    public readonly double N;
    public readonly long I;
    public readonly bool IsInteger;
    public readonly string S = "";

    string DebuggerDisplay
    {
        get
        {
            return $"{Scanner.TokenToString(this)} {T} {N} {S}";
        }
    }
}
