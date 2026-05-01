using System.Text;

namespace Lua.Standard;

public sealed class Utf8Library
{
    public static readonly Utf8Library Instance = new();

    public const string CharPattern = "[\0-\x7F\xC2-\xFD][\x80-\xBF]*";

    public Utf8Library()
    {
        var libraryName = "utf8";
        Functions =
        [
            new(libraryName, "char", Char),
            new(libraryName, "codepoint", CodePoint),
            new(libraryName, "codes", Codes),
            new(libraryName, "len", Len),
            new(libraryName, "offset", Offset)
        ];
    }

    public readonly LibraryFunction[] Functions;

    static byte[] StringToBytes(string s)
    {
        // Strings here are .NET UTF-16. Lua treats strings as opaque byte sequences;
        // when chars are in 0..0xFF (which is how byte-encoded strings appear), we
        // round-trip via Latin-1 to recover the original byte sequence.
        var bytes = new byte[s.Length];
        for (var i = 0; i < s.Length; i++) bytes[i] = (byte)s[i];
        return bytes;
    }

    static string BytesToString(ReadOnlySpan<byte> bytes)
    {
        var arr = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) arr[i] = (char)bytes[i];
        return new string(arr);
    }

    static int Utf8Encode(uint cp, Span<byte> dst)
    {
        if (cp < 0x80) { dst[0] = (byte)cp; return 1; }
        if (cp < 0x800)
        {
            dst[0] = (byte)(0xC0 | (cp >> 6));
            dst[1] = (byte)(0x80 | (cp & 0x3F));
            return 2;
        }
        if (cp < 0x10000)
        {
            dst[0] = (byte)(0xE0 | (cp >> 12));
            dst[1] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            dst[2] = (byte)(0x80 | (cp & 0x3F));
            return 3;
        }
        if (cp < 0x200000)
        {
            dst[0] = (byte)(0xF0 | (cp >> 18));
            dst[1] = (byte)(0x80 | ((cp >> 12) & 0x3F));
            dst[2] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            dst[3] = (byte)(0x80 | (cp & 0x3F));
            return 4;
        }
        if (cp < 0x4000000)
        {
            dst[0] = (byte)(0xF8 | (cp >> 24));
            dst[1] = (byte)(0x80 | ((cp >> 18) & 0x3F));
            dst[2] = (byte)(0x80 | ((cp >> 12) & 0x3F));
            dst[3] = (byte)(0x80 | ((cp >> 6) & 0x3F));
            dst[4] = (byte)(0x80 | (cp & 0x3F));
            return 5;
        }
        dst[0] = (byte)(0xFC | (cp >> 30));
        dst[1] = (byte)(0x80 | ((cp >> 24) & 0x3F));
        dst[2] = (byte)(0x80 | ((cp >> 18) & 0x3F));
        dst[3] = (byte)(0x80 | ((cp >> 12) & 0x3F));
        dst[4] = (byte)(0x80 | ((cp >> 6) & 0x3F));
        dst[5] = (byte)(0x80 | (cp & 0x3F));
        return 6;
    }

    // Decode one codepoint starting at byte index i. Returns (codepoint, byteCount); byteCount = 0 on invalid.
    static (uint cp, int len) Utf8Decode(ReadOnlySpan<byte> bytes, int i)
    {
        if (i >= bytes.Length) return (0, 0);
        byte b0 = bytes[i];
        if (b0 < 0x80) return (b0, 1);
        if (b0 < 0xC2) return (0, 0); // invalid lead

        int need;
        uint cp;
        if (b0 < 0xE0) { need = 1; cp = (uint)(b0 & 0x1F); }
        else if (b0 < 0xF0) { need = 2; cp = (uint)(b0 & 0x0F); }
        else if (b0 < 0xF8) { need = 3; cp = (uint)(b0 & 0x07); }
        else if (b0 < 0xFC) { need = 4; cp = (uint)(b0 & 0x03); }
        else if (b0 < 0xFE) { need = 5; cp = (uint)(b0 & 0x01); }
        else return (0, 0);

        if (i + need >= bytes.Length) return (0, 0);
        for (var k = 1; k <= need; k++)
        {
            byte b = bytes[i + k];
            if ((b & 0xC0) != 0x80) return (0, 0);
            cp = (cp << 6) | (uint)(b & 0x3F);
        }
        return (cp, need + 1);
    }

    public ValueTask<int> Char(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        Span<byte> tmp = stackalloc byte[6];
        for (var i = 0; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument(i);
            if (!arg.TryReadInteger(out var l) || l < 0 || l > 0x7FFFFFFF)
            {
                throw new LuaRuntimeException(context.State, $"bad argument #{i + 1} to 'char' (value out of range)");
            }
            var n = Utf8Encode((uint)l, tmp);
            for (var k = 0; k < n; k++) sb.Append((char)tmp[k]);
        }
        return new(context.Return(sb.ToString()));
    }

    public ValueTask<int> CodePoint(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var bytes = StringToBytes(s);
        long iArg = context.HasArgument(1) ? (long)context.GetArgument<double>(1) : 1;
        long jArg = context.HasArgument(2) ? (long)context.GetArgument<double>(2) : iArg;
        if (iArg < 0) iArg = bytes.Length + 1 + iArg;
        if (jArg < 0) jArg = bytes.Length + 1 + jArg;
        if (iArg < 1 || iArg > bytes.Length || jArg > bytes.Length)
        {
            throw new LuaRuntimeException(context.State, "bad argument to 'codepoint' (out of bounds)");
        }
        var pos = (int)(iArg - 1);
        var endByte = (int)jArg;
        var results = new List<LuaValue>();
        while (pos < endByte)
        {
            var (cp, len) = Utf8Decode(bytes, pos);
            if (len == 0)
            {
                throw new LuaRuntimeException(context.State, "invalid UTF-8 code");
            }
            results.Add(new LuaValue((long)cp));
            pos += len;
        }
        return new(context.Return(results.ToArray()));
    }

    public ValueTask<int> Len(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var bytes = StringToBytes(s);
        long iArg = context.HasArgument(1) ? (long)context.GetArgument<double>(1) : 1;
        long jArg = context.HasArgument(2) ? (long)context.GetArgument<double>(2) : -1;
        if (iArg < 0) iArg = bytes.Length + 1 + iArg;
        if (jArg < 0) jArg = bytes.Length + 1 + jArg;
        if (iArg < 1) iArg = 1;
        if (jArg > bytes.Length) jArg = bytes.Length;

        var pos = (int)(iArg - 1);
        var endByte = (int)jArg;
        long count = 0;
        while (pos < endByte)
        {
            var (_, len) = Utf8Decode(bytes, pos);
            if (len == 0)
            {
                // Return (nil, position-of-first-bad-byte)
                return new(context.Return(LuaValue.Nil, new LuaValue((long)(pos + 1))));
            }
            count++;
            pos += len;
        }
        return new(context.Return(count));
    }

    public ValueTask<int> Offset(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var bytes = StringToBytes(s);
        long n = (long)context.GetArgument<double>(1);
        long iArg = context.HasArgument(2)
            ? (long)context.GetArgument<double>(2)
            : (n >= 0 ? 1 : bytes.Length + 1);
        if (iArg < 0) iArg = bytes.Length + 1 + iArg;
        if (iArg < 1 || iArg > bytes.Length + 1)
        {
            throw new LuaRuntimeException(context.State, "bad argument to 'offset' (position out of bounds)");
        }
        var pos = (int)(iArg - 1);

        if (n == 0)
        {
            // Position of beginning of the codepoint that contains byte at iArg.
            while (pos > 0 && (bytes[pos] & 0xC0) == 0x80) pos--;
            return new(context.Return((long)(pos + 1)));
        }

        if (n > 0)
        {
            n--;
            while (n > 0 && pos < bytes.Length)
            {
                pos++;
                while (pos < bytes.Length && (bytes[pos] & 0xC0) == 0x80) pos++;
                n--;
            }
            if (n == 0) return new(context.Return((long)(pos + 1)));
            return new(context.Return(LuaValue.Nil));
        }

        // n < 0
        while (n < 0 && pos > 0)
        {
            pos--;
            while (pos > 0 && (bytes[pos] & 0xC0) == 0x80) pos--;
            n++;
        }
        if (n == 0) return new(context.Return((long)(pos + 1)));
        return new(context.Return(LuaValue.Nil));
    }

    public ValueTask<int> Codes(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        // Return iterator function, the string, and starting position 0.
        var iter = new LuaFunction("utf8codes_iter", (c, ct) =>
        {
            var str = c.GetArgument<string>(0);
            var pos = (long)c.GetArgument<double>(1);
            var bytes = StringToBytes(str);
            int bytePos;
            if (pos == 0) bytePos = 0;
            else
            {
                bytePos = (int)(pos - 1);
                var (_, plen) = Utf8Decode(bytes, bytePos);
                if (plen == 0)
                {
                    throw new LuaRuntimeException(c.State, "invalid UTF-8 code");
                }
                bytePos += plen;
            }
            if (bytePos >= bytes.Length)
            {
                return new(c.Return(LuaValue.Nil));
            }
            var (cp, _) = Utf8Decode(bytes, bytePos);
            return new(c.Return(new LuaValue((long)(bytePos + 1)), new LuaValue((long)cp)));
        });
        return new(context.Return(iter, s, 0L));
    }
}
