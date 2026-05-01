using System.Text;
using System.Globalization;
using System.Diagnostics;
using Lua.Internal;
using Lua.Runtime;
using Lua.Standard.Internal;

namespace Lua.Standard;

public sealed class StringLibrary
{
    public static readonly StringLibrary Instance = new();

    public StringLibrary()
    {
        var libraryName = "string";
        Functions =
        [
            new(libraryName, "byte", Byte),
            new(libraryName, "char", Char),
            new(libraryName, "dump", Dump),
            new(libraryName, "find", Find),
            new(libraryName, "format", Format),
            new(libraryName, "gmatch", GMatch),
            new(libraryName, "gsub", GSub),
            new(libraryName, "len", Len),
            new(libraryName, "lower", Lower),
            new(libraryName, "match", Match),
            new(libraryName, "rep", Rep),
            new(libraryName, "reverse", Reverse),
            new(libraryName, "sub", Sub),
            new(libraryName, "upper", Upper),
            new(libraryName, "pack", Pack),
            new(libraryName, "unpack", Unpack),
            new(libraryName, "packsize", PackSize)
        ];
    }

    static (bool little, int alignment, int defaultIntSize) ReadFmtPrefix(string fmt, ref int i)
    {
        // Defaults: native endianness (we treat as little), no max alignment, default int size = 4.
        bool little = BitConverter.IsLittleEndian;
        int align = 1;
        // Process leading endianness/alignment markers; they may appear anywhere but we read at start.
        while (i < fmt.Length)
        {
            switch (fmt[i])
            {
                case '<': little = true; i++; break;
                case '>': little = false; i++; break;
                case '=': little = BitConverter.IsLittleEndian; i++; break;
                case '!':
                    {
                        i++;
                        var n = 0;
                        while (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9') { n = n * 10 + (fmt[i] - '0'); i++; }
                        if (n == 0) n = 8;
                        align = n;
                        break;
                    }
                case ' ': i++; break;
                default: return (little, align, 4);
            }
        }
        return (little, align, 4);
    }

    static int ReadOptionalSize(string fmt, ref int i, int defaultSize)
    {
        if (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9')
        {
            var n = 0;
            while (i < fmt.Length && fmt[i] >= '0' && fmt[i] <= '9') { n = n * 10 + (fmt[i] - '0'); i++; }
            return n;
        }
        return defaultSize;
    }

    static void WriteSignedInt(List<byte> dst, long value, int size, bool little)
    {
        Span<byte> tmp = stackalloc byte[8];
        for (int b = 0; b < 8; b++) tmp[b] = (byte)(value >> (b * 8));
        if (little) for (var b = 0; b < size; b++) dst.Add(tmp[b]);
        else for (var b = size - 1; b >= 0; b--) dst.Add(tmp[b]);
    }

    static long ReadSignedInt(ReadOnlySpan<byte> src, int size, bool little)
    {
        ulong v = 0;
        if (little)
        {
            for (var b = 0; b < size; b++) v |= ((ulong)src[b]) << (b * 8);
        }
        else
        {
            for (var b = 0; b < size; b++) v = (v << 8) | src[b];
        }
        // Sign-extend
        if (size < 8 && (v & (1UL << (size * 8 - 1))) != 0)
        {
            v |= ulong.MaxValue << (size * 8);
        }
        return unchecked((long)v);
    }

    static ulong ReadUnsignedInt(ReadOnlySpan<byte> src, int size, bool little)
    {
        ulong v = 0;
        if (little) for (var b = 0; b < size; b++) v |= ((ulong)src[b]) << (b * 8);
        else for (var b = 0; b < size; b++) v = (v << 8) | src[b];
        return v;
    }

    public ValueTask<int> Pack(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fmt = context.GetArgument<string>(0);
        var bytes = new List<byte>();
        var argIdx = 1;
        int i = 0;
        var (little, _, defInt) = ReadFmtPrefix(fmt, ref i);
        while (i < fmt.Length)
        {
            char op = fmt[i++];
            switch (op)
            {
                case '<': little = true; break;
                case '>': little = false; break;
                case '=': little = BitConverter.IsLittleEndian; break;
                case '!': ReadOptionalSize(fmt, ref i, 8); break;
                case ' ': break;
                case 'b': WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), 1, little); break;
                case 'B': WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), 1, little); break;
                case 'h': WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), 2, little); break;
                case 'H': WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), 2, little); break;
                case 'i':
                case 'I':
                    {
                        var sz = ReadOptionalSize(fmt, ref i, defInt);
                        WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), sz, little);
                        break;
                    }
                case 'l':
                case 'L':
                case 'j':
                case 'J':
                    WriteSignedInt(bytes, (long)context.GetArgument<double>(argIdx++), 8, little);
                    break;
                case 'f':
                    {
                        var f = (float)context.GetArgument<double>(argIdx++);
                        var fb = BitConverter.GetBytes(f);
                        if (BitConverter.IsLittleEndian != little) Array.Reverse(fb);
                        bytes.AddRange(fb);
                        break;
                    }
                case 'd':
                case 'n':
                    {
                        var d = context.GetArgument<double>(argIdx++);
                        var fb = BitConverter.GetBytes(d);
                        if (BitConverter.IsLittleEndian != little) Array.Reverse(fb);
                        bytes.AddRange(fb);
                        break;
                    }
                case 'x': bytes.Add(0); break;
                case 's':
                    {
                        var sz = ReadOptionalSize(fmt, ref i, 8);
                        var s = context.GetArgument<string>(argIdx++);
                        WriteSignedInt(bytes, s.Length, sz, little);
                        for (var k = 0; k < s.Length; k++) bytes.Add((byte)s[k]);
                        break;
                    }
                case 'z':
                    {
                        var s = context.GetArgument<string>(argIdx++);
                        for (var k = 0; k < s.Length; k++) bytes.Add((byte)s[k]);
                        bytes.Add(0);
                        break;
                    }
                default:
                    throw new LuaRuntimeException(context.State, $"invalid format option '{op}'");
            }
        }
        var ch = new char[bytes.Count];
        for (var k = 0; k < bytes.Count; k++) ch[k] = (char)bytes[k];
        return new(context.Return(new string(ch)));
    }

    public ValueTask<int> PackSize(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fmt = context.GetArgument<string>(0);
        long size = 0;
        int i = 0;
        var (_, _, defInt) = ReadFmtPrefix(fmt, ref i);
        while (i < fmt.Length)
        {
            char op = fmt[i++];
            switch (op)
            {
                case '<': case '>': case '=': case ' ': break;
                case '!': ReadOptionalSize(fmt, ref i, 8); break;
                case 'b': case 'B': size += 1; break;
                case 'h': case 'H': size += 2; break;
                case 'i': case 'I':
                    size += ReadOptionalSize(fmt, ref i, defInt); break;
                case 'l': case 'L': case 'j': case 'J': size += 8; break;
                case 'f': size += 4; break;
                case 'd': case 'n': size += 8; break;
                case 'x': size += 1; break;
                case 's': case 'z':
                    throw new LuaRuntimeException(context.State, "variable-size format in packsize");
                default:
                    throw new LuaRuntimeException(context.State, $"invalid format option '{op}'");
            }
        }
        return new(context.Return(size));
    }

    public ValueTask<int> Unpack(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var fmt = context.GetArgument<string>(0);
        var s = context.GetArgument<string>(1);
        long startPos = context.HasArgument(2) ? (long)context.GetArgument<double>(2) : 1;
        var bytes = new byte[s.Length];
        for (var k = 0; k < s.Length; k++) bytes[k] = (byte)s[k];
        int idx = (int)(startPos - 1);
        int fi = 0;
        var (little, _, defInt) = ReadFmtPrefix(fmt, ref fi);
        var results = new List<LuaValue>();
        while (fi < fmt.Length)
        {
            char op = fmt[fi++];
            switch (op)
            {
                case '<': little = true; break;
                case '>': little = false; break;
                case '=': little = BitConverter.IsLittleEndian; break;
                case '!': ReadOptionalSize(fmt, ref fi, 8); break;
                case ' ': break;
                case 'b': results.Add(new LuaValue(ReadSignedInt(bytes.AsSpan(idx), 1, little))); idx += 1; break;
                case 'B': results.Add(new LuaValue((long)ReadUnsignedInt(bytes.AsSpan(idx), 1, little))); idx += 1; break;
                case 'h': results.Add(new LuaValue(ReadSignedInt(bytes.AsSpan(idx), 2, little))); idx += 2; break;
                case 'H': results.Add(new LuaValue((long)ReadUnsignedInt(bytes.AsSpan(idx), 2, little))); idx += 2; break;
                case 'i':
                    {
                        var sz = ReadOptionalSize(fmt, ref fi, defInt);
                        results.Add(new LuaValue(ReadSignedInt(bytes.AsSpan(idx), sz, little)));
                        idx += sz;
                        break;
                    }
                case 'I':
                    {
                        var sz = ReadOptionalSize(fmt, ref fi, defInt);
                        results.Add(new LuaValue((long)ReadUnsignedInt(bytes.AsSpan(idx), sz, little)));
                        idx += sz;
                        break;
                    }
                case 'l': case 'j': results.Add(new LuaValue(ReadSignedInt(bytes.AsSpan(idx), 8, little))); idx += 8; break;
                case 'L': case 'J': results.Add(new LuaValue((long)ReadUnsignedInt(bytes.AsSpan(idx), 8, little))); idx += 8; break;
                case 'f':
                    {
                        var fb = bytes.AsSpan(idx, 4).ToArray();
                        if (BitConverter.IsLittleEndian != little) Array.Reverse(fb);
                        results.Add(new LuaValue((double)BitConverter.ToSingle(fb, 0)));
                        idx += 4;
                        break;
                    }
                case 'd':
                case 'n':
                    {
                        var fb = bytes.AsSpan(idx, 8).ToArray();
                        if (BitConverter.IsLittleEndian != little) Array.Reverse(fb);
                        results.Add(new LuaValue(BitConverter.ToDouble(fb, 0)));
                        idx += 8;
                        break;
                    }
                case 'x': idx += 1; break;
                case 's':
                    {
                        var sz = ReadOptionalSize(fmt, ref fi, 8);
                        long len = ReadSignedInt(bytes.AsSpan(idx), sz, little);
                        idx += sz;
                        var sb = new char[len];
                        for (var k = 0; k < len; k++) sb[k] = (char)bytes[idx + k];
                        results.Add(new string(sb));
                        idx += (int)len;
                        break;
                    }
                case 'z':
                    {
                        int z = idx;
                        while (z < bytes.Length && bytes[z] != 0) z++;
                        var sb = new char[z - idx];
                        for (var k = 0; k < sb.Length; k++) sb[k] = (char)bytes[idx + k];
                        results.Add(new string(sb));
                        idx = z + 1;
                        break;
                    }
                default:
                    throw new LuaRuntimeException(context.State, $"invalid format option '{op}'");
            }
        }
        results.Add(new LuaValue((long)(idx + 1)));
        return new(context.Return(results.ToArray()));
    }

    public readonly LibraryFunction[] Functions;

    public ValueTask<int> Byte(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var i = context.HasArgument(1)
            ? context.GetArgument<double>(1)
            : 1;
        var j = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : i;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, i);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 3, j);

        var span = StringHelper.Slice(s, (int)i, (int)j);
        var buffer = context.GetReturnBuffer(span.Length);
        for (var k = 0; k < span.Length; k++)
        {
            buffer[k] = span[k];
        }

        return new(span.Length);
    }

    public ValueTask<int> Char(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.ArgumentCount == 0)
        {
            return new(context.Return(""));
        }

        ValueStringBuilder builder = new(context.ArgumentCount);
        for (var i = 0; i < context.ArgumentCount; i++)
        {
            var arg = context.GetArgument<double>(i);
            LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, i + 1, arg);
            builder.Append((char)arg);
        }

        return new(context.Return(builder.ToString()));
    }

    public ValueTask<int> Dump(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("stirng.dump is not supported");
    }

    public ValueTask<int> Find(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return FindAux(context, true);
    }

    public async ValueTask<int> Format(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var format = context.GetArgument<string>(0);
        var stack = context.State.Stack;
        // TODO: pooling StringBuilder
        StringBuilder builder = new(format.Length * 2);
        var parameterIndex = 1;

        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] == '%')
            {
                i++;

                // escape
                if (format[i] == '%')
                {
                    builder.Append('%');
                    continue;
                }

                var leftJustify = false;
                var plusSign = false;
                var zeroPadding = false;
                var alternateForm = false;
                var blank = false;
                var width = 0;
                var precision = -1;

                // Process flags
                while (true)
                {
                    var c = format[i];
                    switch (c)
                    {
                        case '-':
                            if (leftJustify)
                            {
                                throw new LuaRuntimeException(context.State, "invalid format (repeated flags)");
                            }

                            leftJustify = true;
                            break;
                        case '+':
                            if (plusSign)
                            {
                                throw new LuaRuntimeException(context.State, "invalid format (repeated flags)");
                            }

                            plusSign = true;
                            break;
                        case '0':
                            if (zeroPadding)
                            {
                                throw new LuaRuntimeException(context.State, "invalid format (repeated flags)");
                            }

                            zeroPadding = true;
                            break;
                        case '#':
                            if (alternateForm)
                            {
                                throw new LuaRuntimeException(context.State, "invalid format (repeated flags)");
                            }

                            alternateForm = true;
                            break;
                        case ' ':
                            if (blank)
                            {
                                throw new LuaRuntimeException(context.State, "invalid format (repeated flags)");
                            }

                            blank = true;
                            break;
                        default:
                            goto PROCESS_WIDTH;
                    }

                    i++;
                }

            PROCESS_WIDTH:

                // Process width
                var start = i;
                if (char.IsDigit(format[i]))
                {
                    i++;
                    if (char.IsDigit(format[i]))
                    {
                        i++;
                    }

                    if (char.IsDigit(format[i]))
                    {
                        throw new LuaRuntimeException(context.State, "invalid format (width or precision too long)");
                    }

                    width = int.Parse(format.AsSpan()[start..i]);
                }

                // Process precision
                if (format[i] == '.')
                {
                    i++;
                    start = i;
                    if (char.IsDigit(format[i]))
                    {
                        i++;
                    }

                    if (char.IsDigit(format[i]))
                    {
                        i++;
                    }

                    if (char.IsDigit(format[i]))
                    {
                        throw new LuaRuntimeException(context.State, "invalid format (width or precision too long)");
                    }

                    precision = int.Parse(format.AsSpan()[start..i]);
                }

                // Process conversion specifier
                var specifier = format[i];

                if (context.ArgumentCount <= parameterIndex)
                {
                    throw new LuaRuntimeException(context.State, $"bad argument #{parameterIndex + 1} to 'format' (no value)");
                }

                var parameter = context.GetArgument(parameterIndex++);

                // TODO: reduce allocation
                string formattedValue = default!;
                switch (specifier)
                {
                    case 'f':
                    case 'e':
                    case 'g':
                    case 'G':
                        if (!parameter.TryRead<double>(out var f))
                        {
                            LuaRuntimeException.BadArgument(context.State, parameterIndex + 1, LuaValueType.Number, parameter.Type);
                        }

                        switch (specifier)
                        {
                            case 'f':
                                formattedValue = precision < 0
                                    ? f.ToString(CultureInfo.InvariantCulture)
                                    : f.ToString($"F{precision}", CultureInfo.InvariantCulture);
                                break;
                            case 'e':
                                formattedValue = precision < 0
                                    ? f.ToString(CultureInfo.InvariantCulture)
                                    : f.ToString($"E{precision}", CultureInfo.InvariantCulture);
                                break;
                            case 'g':
                                formattedValue = precision < 0
                                    ? f.ToString(CultureInfo.InvariantCulture)
                                    : f.ToString($"G{precision}", CultureInfo.InvariantCulture);
                                break;
                            case 'G':
                                formattedValue = precision < 0
                                    ? f.ToString(CultureInfo.InvariantCulture).ToUpper()
                                    : f.ToString($"G{precision}", CultureInfo.InvariantCulture).ToUpper();
                                break;
                        }

                        if (plusSign && f >= 0)
                        {
                            formattedValue = $"+{formattedValue}";
                        }

                        break;
                    case 's':
                        {
                            await parameter.CallToStringAsync(context, cancellationToken);
                            formattedValue = stack.Pop().Read<string>();
                        }

                        if (specifier is 's' && precision > 0 && precision <= formattedValue.Length)
                        {
                            formattedValue = formattedValue[..precision];
                        }

                        break;
                    case 'q':
                        switch (parameter.Type)
                        {
                            case LuaValueType.Nil:
                                formattedValue = "nil";
                                break;
                            case LuaValueType.Boolean:
                                formattedValue = parameter.Read<bool>() ? "true" : "false";
                                break;
                            case LuaValueType.String:
                                formattedValue = $"\"{StringHelper.Escape(parameter.Read<string>())}\"";
                                break;
                            case LuaValueType.Number:
                                formattedValue = DoubleToQFormat(parameter.Read<double>());

                                static string DoubleToQFormat(double value)
                                {
                                    if (MathEx.IsInteger(value))
                                    {
                                        return value.ToString(CultureInfo.InvariantCulture);
                                    }

                                    return HexConverter.FromDouble(value);
                                }

                                break;
                            default:

                                {
                                    var top = stack.Count;
                                    stack.Push(default);
                                    await parameter.CallToStringAsync(context with { ReturnFrameBase = top }, cancellationToken);
                                    formattedValue = stack.Pop().Read<string>();
                                }
                                break;
                        }

                        break;
                    case 'i':
                    case 'd':
                    case 'u':
                    case 'c':
                    case 'x':
                    case 'X':
                        if (!parameter.TryRead<double>(out var x))
                        {
                            LuaRuntimeException.BadArgument(context.State, parameterIndex + 1, LuaValueType.Number, parameter.Type);
                        }

                        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, parameterIndex + 1, x);

                        switch (specifier)
                        {
                            case 'i':
                            case 'd':
                                {
                                    var integer = checked((long)x);
                                    formattedValue = precision < 0
                                        ? integer.ToString()
                                        : integer.ToString($"D{precision}");
                                }
                                break;
                            case 'u':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = precision < 0
                                        ? integer.ToString()
                                        : integer.ToString($"D{precision}");
                                }
                                break;
                            case 'c':
                                formattedValue = ((char)(int)x).ToString();
                                break;
                            case 'x':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = alternateForm
                                        ? $"0x{integer:x}"
                                        : $"{integer:x}";
                                }
                                break;
                            case 'X':
                                {
                                    var integer = checked((ulong)x);
                                    formattedValue = alternateForm
                                        ? $"0X{integer:X}"
                                        : $"{integer:X}";
                                }
                                break;
                            case 'o':
                                {
                                    var integer = checked((long)x);
                                    formattedValue = Convert.ToString(integer, 8);
                                }
                                break;
                        }

                        if (plusSign && x >= 0)
                        {
                            formattedValue = $"+{formattedValue}";
                        }

                        break;
                    default:
                        throw new LuaRuntimeException(context.State, $"invalid option '%{specifier}' to 'format'");
                }

                // Apply blank (' ') flag for positive numbers
                if (specifier is 'd' or 'i' or 'f' or 'g' or 'G')
                {
                    if (blank && !leftJustify && !zeroPadding && parameter.Read<double>() >= 0)
                    {
                        formattedValue = $" {formattedValue}";
                    }
                }

                // Apply width and padding
                if (width > formattedValue.Length)
                {
                    if (leftJustify)
                    {
                        formattedValue = formattedValue.PadRight(width);
                    }
                    else
                    {
                        formattedValue = zeroPadding ? formattedValue.PadLeft(width, '0') : formattedValue.PadLeft(width);
                    }
                }

                builder.Append(formattedValue);
            }
            else
            {
                builder.Append(format[i]);
            }
        }

        return context.Return(builder.ToString());
    }

    public ValueTask<int> GMatch(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);

        return new(context.Return(new CSharpClosure("gmatch_iterator", [s, pattern, 0], static (context, cancellationToken) =>
        {
            var upValues = context.GetCsClosure()!.UpValues;
            var s = upValues[0].Read<string>();
            var pattern = upValues[1].Read<string>();
            var start = upValues[2].Read<int>();

            MatchState matchState = new(context.State, s, pattern);
            var captures = matchState.Captures;

            // Check for anchor at start
            var anchor = pattern.Length > 0 && pattern[0] == '^';
            var pIdx = anchor ? 1 : 0;

            // For empty patterns, we need to match at every position including after the last character
            var sEndIdx = s.Length + (pattern.Length == 0 || (anchor && pattern.Length == 1) ? 1 : 0);

            for (var sIdx = start; sIdx < sEndIdx; sIdx++)
            {
                // Reset match state for each attempt
                matchState.Level = 0;
                matchState.MatchDepth = MatchState.MaxCalls;
                // Clear captures to avoid stale data
                Array.Clear(captures, 0, captures.Length);

                var res = matchState.Match(sIdx, pIdx);

                if (res >= 0)
                {
                    // If no captures were made, create one for the whole match
                    if (matchState.Level == 0)
                    {
                        captures[0].Init = sIdx;
                        captures[0].Len = res - sIdx;
                        matchState.Level = 1;
                    }

                    var resultLength = matchState.Level;
                    var buffer = context.GetReturnBuffer(resultLength);
                    for (var i = 0; i < matchState.Level; i++)
                    {
                        var capture = captures[i];
                        if (capture.IsPosition)
                        {
                            buffer[i] = capture.Init + 1; // 1-based position
                        }
                        else
                        {
                            buffer[i] = s.AsSpan(capture.Init, capture.Len).ToString();
                        }
                    }

                    // Update start index for next iteration
                    // Handle empty matches by advancing at least 1 position
                    upValues[2] = res > sIdx ? res : sIdx + 1;
                    return new(resultLength);
                }

                // For anchored patterns, only try once
                if (anchor)
                {
                    break;
                }
            }

            return new(context.Return(LuaValue.Nil));
        })));
    }


    public async ValueTask<int> GSub(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);
        var repl = context.GetArgument(2);
        var n_arg = context.HasArgument(3)
            ? context.GetArgument<double>(3)
            : s.Length + 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 4, n_arg);

        var n = (int)n_arg;

        // Use MatchState instead of regex
        MatchState matchState = new(context.State, s, pattern);
        var captures = matchState.Captures;

        StringBuilder builder = new();
        var replacedBuilder = repl.Type == LuaValueType.String
            ? new StringBuilder(repl.UnsafeReadString().Length)
            : null;
        var lastIndex = 0;
        var replaceCount = 0;

        // Check for anchor at start
        var anchor = pattern.Length > 0 && pattern[0] == '^';
        var sIdx = 0;

        // For empty patterns, we need to match at every position including after the last character
        var sEndIdx = s.Length + (pattern.Length == 0 || (anchor && pattern.Length == 1) ? 1 : 0);
        while (sIdx < sEndIdx && replaceCount < n)
        {
            // Reset match state for each attempt
            matchState.Level = 0;
            Debug.Assert(matchState.MatchDepth == MatchState.MaxCalls);
            // Clear captures array to avoid stale data
            for (var i = 0; i < captures.Length; i++)
            {
                captures[i] = default;
            }

            // Always start pattern from beginning (0 or 1 if anchored)
            var pIdx = anchor ? 1 : 0;
            var res = matchState.Match(sIdx, pIdx);

            if (res >= 0)
            {
                // Found a match
                builder.Append(s.AsSpan()[lastIndex..sIdx]);

                // If no captures were made, create one for the whole match
                if (matchState.Level == 0)
                {
                    captures[0].Init = sIdx;
                    captures[0].Len = res - sIdx;
                    matchState.Level = 1;
                }

                LuaValue result;
                if (repl.TryRead<string>(out var str))
                {
                    if (!str.Contains("%"))
                    {
                        result = str; // No special characters, use as is
                    }
                    else
                    {
                        // String replacement
                        replacedBuilder!.Clear();
                        replacedBuilder.Append(str);

                        // Replace %% with %
                        replacedBuilder.Replace("%%", "\0"); // Use null char as temporary marker

                        // Replace %0 with whole match
                        var wholeMatch = s.AsSpan(sIdx, res - sIdx).ToString();
                        replacedBuilder.Replace("%0", wholeMatch);

                        // Replace %1, %2, etc. with captures
                        for (var k = 0; k < matchState.Level; k++)
                        {
                            var capture = captures[k];
                            string captureText;

                            if (capture.IsPosition)
                            {
                                captureText = (capture.Init + 1).ToString(); // 1-based position
                            }
                            else
                            {
                                captureText = s.AsSpan(capture.Init, capture.Len).ToString();
                            }

                            replacedBuilder.Replace($"%{k + 1}", captureText);
                        }

                        // Replace temporary marker back to %
                        replacedBuilder.Replace('\0', '%');
                        result = replacedBuilder.ToString();
                    }
                }
                else if (repl.TryRead<LuaTable>(out var table))
                {
                    // Table lookup - use first capture or whole match
                    string key;
                    if (matchState.Level > 0 && !captures[0].IsPosition)
                    {
                        key = s.AsSpan(captures[0].Init, captures[0].Len).ToString();
                    }
                    else
                    {
                        key = s.AsSpan(sIdx, res - sIdx).ToString();
                    }

                    result = table[key];
                }
                else if (repl.TryRead<LuaFunction>(out var func))
                {
                    // Function call with captures as arguments
                    var stack = context.State.Stack;

                    if (matchState.Level == 0)
                    {
                        // No captures, pass whole match
                        stack.Push(s.AsSpan(sIdx, res - sIdx).ToString());
                        var retCount = await context.State.RunAsync(func, 1, cancellationToken);
                        using var results = context.State.ReadStack(retCount);
                        result = results.Count > 0 ? results[0] : LuaValue.Nil;
                    }
                    else
                    {
                        // Pass all captures
                        for (var k = 0; k < matchState.Level; k++)
                        {
                            var capture = captures[k];
                            if (capture.IsPosition)
                            {
                                stack.Push(capture.Init + 1); // 1-based position
                            }
                            else
                            {
                                stack.Push(s.AsSpan(capture.Init, capture.Len).ToString());
                            }
                        }

                        var retCount = await context.State.RunAsync(func, matchState.Level, cancellationToken);
                        using var results = context.State.ReadStack(retCount);
                        result = results.Count > 0 ? results[0] : LuaValue.Nil;
                    }
                }
                else
                {
                    throw new LuaRuntimeException(context.State, "bad argument #3 to 'gsub' (string/function/table expected)");
                }

                // Handle replacement result
                if (result.TryRead<string>(out var rs))
                {
                    builder.Append(rs);
                }
                else if (result.TryRead<double>(out var rd))
                {
                    builder.Append(rd);
                }
                else if (!result.ToBoolean())
                {
                    // False or nil means don't replace
                    builder.Append(s.AsSpan(sIdx, res - sIdx));
                }
                else
                {
                    throw new LuaRuntimeException(context.State, $"invalid replacement value (a {result.Type})");
                }

                replaceCount++;
                lastIndex = res;

                // If empty match, advance by 1 to avoid infinite loop
                if (res == sIdx)
                {
                    if (sIdx < s.Length)
                    {
                        builder.Append(s[sIdx]);
                        lastIndex = sIdx + 1;
                    }

                    sIdx++;
                }
                else
                {
                    sIdx = res;
                }
            }
            else
            {
                // No match at this position
                if (anchor)
                {
                    // Anchored pattern only tries at start
                    break;
                }

                sIdx++;
            }
        }

        // Append remaining part of string
        if (lastIndex < s.Length)
        {
            builder.Append(s.AsSpan()[lastIndex..]);
        }

        return context.Return(builder.ToString(), replaceCount);
    }

    public ValueTask<int> Len(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        return new(context.Return(s.Length));
    }

    public ValueTask<int> Lower(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        return new(context.Return(s.ToLower()));
    }

    public ValueTask<int> Match(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        return FindAux(context, false);
    }

    public ValueTask<int> FindAux(LuaFunctionExecutionContext context, bool find)
    {
        var s = context.GetArgument<string>(0);
        var pattern = context.GetArgument<string>(1);
        var init = context.HasArgument(2)
            ? context.GetArgument<int>(2)
            : 1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 3, init);

        // Convert to 0-based index
        if (init < 0)
        {
            init = s.Length + init + 1;
        }

        init--; // Convert from 1-based to 0-based

        // Check if init is beyond string bounds
        if (init > s.Length)
        {
            return new(context.Return(LuaValue.Nil));
        }

        init = Math.Max(0, init); // Clamp to 0 if negative

        // Check for plain search mode (4th parameter = true) or if pattern has no special characters
        if (find && (context.GetArgumentOrDefault(3).ToBoolean() || MatchState.NoSpecials(pattern)))
        {
            return PlainSearch(context, s, pattern, init);
        }

        return PatternSearch(context, s, pattern, init, find);
    }

    static ValueTask<int> PlainSearch(LuaFunctionExecutionContext context, string s, string pattern, int init)
    {
        var index = s.AsSpan(init).IndexOf(pattern);
        if (index == -1)
        {
            return new(context.Return(LuaValue.Nil));
        }

        var actualStart = init + index;
        return new(context.Return(actualStart + 1, actualStart + pattern.Length)); // Convert to 1-based
    }

    static ValueTask<int> PatternSearch(LuaFunctionExecutionContext context, string s, string pattern, int init, bool find)
    {
        MatchState matchState = new(context.State, s, pattern);
        var captures = matchState.Captures;

        // Check for anchor at start
        var anchor = pattern.Length > 0 && pattern[0] == '^';
        var pIdx = anchor ? 1 : 0;

        // For empty patterns, we need to match at every position including after the last character
        var sEndIdx = s.Length + (pattern.Length == 0 ? 1 : 0);

        for (var sIdx = init; sIdx < sEndIdx; sIdx++)
        {
            // Reset match state for each attempt
            matchState.Level = 0;
            matchState.MatchDepth = MatchState.MaxCalls;
            Array.Clear(captures, 0, captures.Length);

            var res = matchState.Match(sIdx, pIdx);

            if (res >= 0)
            {
                // If no captures were made for string.match, create one for the whole match
                if (!find && matchState.Level == 0)
                {
                    captures[0].Init = sIdx;
                    captures[0].Len = res - sIdx;
                    matchState.Level = 1;
                }

                var resultLength = matchState.Level + (find ? 2 : 0);
                var buffer = context.GetReturnBuffer(resultLength);

                if (find)
                {
                    // Return start and end positions for string.find
                    buffer[0] = sIdx + 1; // Convert to 1-based index
                    buffer[1] = res; // Convert to 1-based index
                    buffer = buffer[2..];
                }

                // Return captures
                for (var i = 0; i < matchState.Level; i++)
                {
                    var capture = captures[i];
                    if (capture.IsPosition)
                    {
                        buffer[i] = capture.Init + 1; // 1-based position
                    }
                    else
                    {
                        buffer[i] = s.AsSpan(capture.Init, capture.Len).ToString();
                    }
                }

                return new(resultLength);
            }

            // For anchored patterns, only try once
            if (anchor)
            {
                break;
            }
        }

        return new(context.Return(LuaValue.Nil));
    }

    public ValueTask<int> Rep(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var n_arg = context.GetArgument<double>(1);
        var sep = context.HasArgument(2)
            ? context.GetArgument<string>(2)
            : null;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, n_arg);

        var n = (int)n_arg;

        ValueStringBuilder builder = new(s.Length * n);
        for (var i = 0; i < n; i++)
        {
            builder.Append(s);
            if (i != n - 1 && sep != null)
            {
                builder.Append(sep);
            }
        }

        return new(context.Return(builder.ToString()));
    }

    public ValueTask<int> Reverse(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        using PooledArray<char> strBuffer = new(s.Length);
        var span = strBuffer.AsSpan()[..s.Length];
        s.AsSpan().CopyTo(span);
        span.Reverse();
        return new(context.Return(span.ToString()));
    }

    public ValueTask<int> Sub(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        var i = context.GetArgument<double>(1);
        var j = context.HasArgument(2)
            ? context.GetArgument<double>(2)
            : -1;

        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 2, i);
        LuaRuntimeException.ThrowBadArgumentIfNumberIsNotInteger(context.State, 3, j);

        return new(context.Return(StringHelper.Slice(s, (int)i, (int)j).ToString()));
    }

    public ValueTask<int> Upper(LuaFunctionExecutionContext context, CancellationToken cancellationToken)
    {
        var s = context.GetArgument<string>(0);
        return new(context.Return(s.ToUpper()));
    }
}