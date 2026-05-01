using Lua.Standard;

namespace Lua.Tests;

// Unit tests for Lua 5.3 features added in this fork.
public class Lua53FeaturesTests
{
    static LuaState CreateState()
    {
        var s = LuaState.Create();
        s.OpenStandardLibraries();
        return s;
    }

    // --- Integer subtype: LuaValue ---------------------------------------------------

    [Test]
    public void LuaValue_IntegerAndFloat_AreEqual()
    {
        var i = new LuaValue(1L);
        var f = new LuaValue(1.0);
        Assert.That(i, Is.EqualTo(f));
        Assert.That(i.GetHashCode(), Is.EqualTo(f.GetHashCode()));
    }

    [Test]
    public void LuaValue_IntegerSubtype_IsObservable()
    {
        var i = new LuaValue(7L);
        var f = new LuaValue(7.0);
        Assert.That(i.IsInteger, Is.True);
        Assert.That(i.IsFloat, Is.False);
        Assert.That(f.IsInteger, Is.False);
        Assert.That(f.IsFloat, Is.True);
    }

    [Test]
    public void LuaValue_LargeIntegerNotEqualToRoundedFloat()
    {
        // 2^53 + 1 cannot be exactly represented as a double; the integer must
        // not compare equal to its nearest float.
        long big = (1L << 53) + 1;
        var i = new LuaValue(big);
        var f = new LuaValue((double)(1L << 53));
        Assert.That(i, Is.Not.EqualTo(f));
    }

    [Test]
    public void LuaValue_TryReadInteger_ConvertsIntegralFloat()
    {
        Assert.That(new LuaValue(3.0).TryReadInteger(out var r), Is.True);
        Assert.That(r, Is.EqualTo(3L));
        Assert.That(new LuaValue(3.5).TryReadInteger(out _), Is.False);
    }

    // --- _VERSION + math.type / tointeger / ult --------------------------------------

    [Test]
    public async Task Version_IsLua53()
    {
        var r = await CreateState().DoStringAsync("return _VERSION");
        Assert.That(r[0].Read<string>(), Is.EqualTo("Lua 5.3"));
    }

    [TestCase("return math.type(1)", "integer")]
    [TestCase("return math.type(1.0)", "float")]
    public async Task MathType(string code, string expected)
    {
        var r = await CreateState().DoStringAsync(code);
        Assert.That(r[0].Read<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task MathType_ReturnsNilForNonNumber()
    {
        var r = await CreateState().DoStringAsync("return math.type('hi')");
        Assert.That(r[0].Type, Is.EqualTo(LuaValueType.Nil));
    }

    [Test]
    public async Task MathTointeger()
    {
        var r = await CreateState().DoStringAsync("return math.tointeger(3.0), math.tointeger(3.5), math.tointeger('42')");
        Assert.That(r[0].Read<long>(), Is.EqualTo(3L));
        Assert.That(r[1].Type, Is.EqualTo(LuaValueType.Nil));
        Assert.That(r[2].Read<long>(), Is.EqualTo(42L));
    }

    [Test]
    public async Task MathMaxMinInteger()
    {
        var r = await CreateState().DoStringAsync("return math.maxinteger, math.mininteger");
        Assert.That(r[0].Read<long>(), Is.EqualTo(long.MaxValue));
        Assert.That(r[1].Read<long>(), Is.EqualTo(long.MinValue));
    }

    [Test]
    public async Task MathUlt()
    {
        var r = await CreateState().DoStringAsync("return math.ult(1, 2), math.ult(-1, 2)");
        Assert.That(r[0].Read<bool>(), Is.True);
        Assert.That(r[1].Read<bool>(), Is.False);
    }

    [Test]
    public async Task MathAtan_TwoArgForm()
    {
        var r = await CreateState().DoStringAsync("return math.atan(1, 1)");
        Assert.That(r[0].Read<double>(), Is.EqualTo(Math.PI / 4).Within(1e-12));
    }

    // --- Integer arithmetic subtype preservation -------------------------------------

    [TestCase("return math.type(1 + 2)", "integer")]
    [TestCase("return math.type(1 + 2.0)", "float")]
    [TestCase("return math.type(2 - 1)", "integer")]
    [TestCase("return math.type(3 * 4)", "integer")]
    [TestCase("return math.type(7 // 2)", "integer")]
    [TestCase("return math.type(7.0 // 2)", "float")]
    [TestCase("return math.type(7 / 2)", "float")]
    [TestCase("return math.type(2 ^ 3)", "float")]
    [TestCase("return math.type(7 % 2)", "integer")]
    public async Task ArithmeticSubtype(string code, string expected)
    {
        var r = await CreateState().DoStringAsync(code);
        Assert.That(r[0].Read<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task FloorDivision_NegativeOperands()
    {
        var r = await CreateState().DoStringAsync("return -7 // 2, 7 // -2");
        Assert.That(r[0].Read<long>(), Is.EqualTo(-4L));
        Assert.That(r[1].Read<long>(), Is.EqualTo(-4L));
    }

    [Test]
    public async Task FloorMod_LuaSemantics()
    {
        // Lua mod is floor-mod: -7 % 2 == 1, 7 % -2 == -1.
        var r = await CreateState().DoStringAsync("return -7 % 2, 7 % -2");
        Assert.That(r[0].Read<long>(), Is.EqualTo(1L));
        Assert.That(r[1].Read<long>(), Is.EqualTo(-1L));
    }

    [Test]
    public async Task IntegerOverflow_Wraps()
    {
        var r = await CreateState().DoStringAsync("return math.maxinteger + 1 == math.mininteger");
        Assert.That(r[0].Read<bool>(), Is.True);
    }

    [Test]
    public async Task Equality_IntAndFloat_AreEqual()
    {
        var r = await CreateState().DoStringAsync("return 1 == 1.0, 0 == 0.0, -3 == -3.0");
        Assert.That(r[0].Read<bool>(), Is.True);
        Assert.That(r[1].Read<bool>(), Is.True);
        Assert.That(r[2].Read<bool>(), Is.True);
    }

    // --- Bitwise operators -----------------------------------------------------------

    [TestCase("return 0xff & 0x0f", 0x0fL)]
    [TestCase("return 0x0f | 0xf0", 0xffL)]
    [TestCase("return 0xff ~ 0x0f", 0xf0L)]
    [TestCase("return ~0", -1L)]
    [TestCase("return ~0xff", -256L)]
    [TestCase("return 1 << 4", 16L)]
    [TestCase("return 256 >> 4", 16L)]
    [TestCase("return 1 << 63", long.MinValue)]
    [TestCase("return 1 << 64", 0L)]
    [TestCase("return 0xff >> 100", 0L)]
    public async Task BitwiseOperators(string code, long expected)
    {
        var r = await CreateState().DoStringAsync(code);
        Assert.That(r[0].Read<long>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task BitwiseOps_PreserveIntegerSubtype()
    {
        var r = await CreateState().DoStringAsync("return math.type(1 & 2), math.type(~0)");
        Assert.That(r[0].Read<string>(), Is.EqualTo("integer"));
        Assert.That(r[1].Read<string>(), Is.EqualTo("integer"));
    }

    [Test]
    public async Task BitwisePrecedence()
    {
        // & is tighter than |, |  is loosest of bit ops.
        var r = await CreateState().DoStringAsync("return 1 | 2 & 1, (1 | 2) & 1, 1 << 2 + 1");
        Assert.That(r[0].Read<long>(), Is.EqualTo(1L));   // 1 | (2 & 1) = 1 | 0 = 1
        Assert.That(r[1].Read<long>(), Is.EqualTo(1L));   // (1 | 2) & 1 = 3 & 1 = 1
        Assert.That(r[2].Read<long>(), Is.EqualTo(8L));   // 1 << (2+1) = 8 — additive tighter than shift
    }

    // --- Bitwise metamethods ---------------------------------------------------------

    [Test]
    public async Task BitwiseMetamethod_Band()
    {
        var r = await CreateState().DoStringAsync(@"
            local t = setmetatable({}, { __band = function(a, b) return 'band-called' end })
            return t & 1
        ");
        Assert.That(r[0].Read<string>(), Is.EqualTo("band-called"));
    }

    [Test]
    public async Task IDivMetamethod()
    {
        var r = await CreateState().DoStringAsync(@"
            local t = setmetatable({}, { __idiv = function(a, b) return 'idiv-called' end })
            return t // 1
        ");
        Assert.That(r[0].Read<string>(), Is.EqualTo("idiv-called"));
    }

    // --- \u{} escape -----------------------------------------------------------------

    [Test]
    public async Task UnicodeEscape_AsciiAndMultibyte()
    {
        var r = await CreateState().DoStringAsync(@"return ""A\u{4E2D}B""");
        var s = r[0].Read<string>();
        // 'A' (1 byte) + UTF-8 of U+4E2D (3 bytes E4 B8 AD) + 'B' (1 byte) = 5 bytes.
        Assert.That(s.Length, Is.EqualTo(5));
        Assert.That((int)s[0], Is.EqualTo('A'));
        Assert.That((int)s[1], Is.EqualTo(0xE4));
        Assert.That((int)s[2], Is.EqualTo(0xB8));
        Assert.That((int)s[3], Is.EqualTo(0xAD));
        Assert.That((int)s[4], Is.EqualTo('B'));
    }

    [Test]
    public async Task UnicodeEscape_LowAndHigh()
    {
        var r = await CreateState().DoStringAsync(@"return #""\u{0}\u{7F}""");
        Assert.That(r[0].Read<long>(), Is.EqualTo(2L));
    }

    // --- utf8 library ----------------------------------------------------------------

    [Test]
    public async Task Utf8_Char_RoundtripsCodepoint()
    {
        var r = await CreateState().DoStringAsync("return utf8.char(0x4E2D)");
        var s = r[0].Read<string>();
        Assert.That(s.Length, Is.EqualTo(3));
        Assert.That((int)s[0], Is.EqualTo(0xE4));
    }

    [Test]
    public async Task Utf8_Len_CountsCodepoints()
    {
        var r = await CreateState().DoStringAsync(@"return utf8.len(""h\u{E9}llo"")");
        Assert.That(r[0].Read<long>(), Is.EqualTo(5L));
    }

    [Test]
    public async Task Utf8_Codepoint()
    {
        var r = await CreateState().DoStringAsync(@"return utf8.codepoint(""\u{4E2D}"")");
        Assert.That(r[0].Read<long>(), Is.EqualTo(0x4E2DL));
    }

    [Test]
    public async Task Utf8_Codes_IteratesCodepoints()
    {
        var r = await CreateState().DoStringAsync(@"
            local cps = {}
            for p, c in utf8.codes(""A\u{4E2D}B"") do
                cps[#cps + 1] = c
            end
            return cps[1], cps[2], cps[3], #cps
        ");
        Assert.That(r[0].Read<long>(), Is.EqualTo((long)'A'));
        Assert.That(r[1].Read<long>(), Is.EqualTo(0x4E2DL));
        Assert.That(r[2].Read<long>(), Is.EqualTo((long)'B'));
        Assert.That(r[3].Read<long>(), Is.EqualTo(3L));
    }

    [Test]
    public async Task Utf8_Offset_AsciiAdvance()
    {
        var r = await CreateState().DoStringAsync(@"return utf8.offset(""abc"", 2)");
        Assert.That(r[0].Read<long>(), Is.EqualTo(2L));
    }

    // --- table.move ------------------------------------------------------------------

    [Test]
    public async Task TableMove_Basic()
    {
        var r = await CreateState().DoStringAsync(@"
            local t = {1, 2, 3, 4, 5}
            table.move(t, 1, 3, 2)  -- copy [1..3] into [2..4]
            return t[1], t[2], t[3], t[4], t[5]
        ");
        Assert.That(r[0].Read<long>(), Is.EqualTo(1L));
        Assert.That(r[1].Read<long>(), Is.EqualTo(1L));
        Assert.That(r[2].Read<long>(), Is.EqualTo(2L));
        Assert.That(r[3].Read<long>(), Is.EqualTo(3L));
        Assert.That(r[4].Read<long>(), Is.EqualTo(5L));
    }

    [Test]
    public async Task TableMove_SeparateDestination()
    {
        var r = await CreateState().DoStringAsync(@"
            local a = {10, 20, 30}
            local b = {99, 99, 99}
            table.move(a, 1, 3, 1, b)
            return b[1], b[2], b[3]
        ");
        Assert.That(r[0].Read<long>(), Is.EqualTo(10L));
        Assert.That(r[1].Read<long>(), Is.EqualTo(20L));
        Assert.That(r[2].Read<long>(), Is.EqualTo(30L));
    }

    // --- coroutine.isyieldable -------------------------------------------------------

    [Test]
    public async Task Coroutine_IsYieldable_MainIsFalse()
    {
        var r = await CreateState().DoStringAsync("return coroutine.isyieldable()");
        Assert.That(r[0].Read<bool>(), Is.False);
    }

    [Test]
    public async Task Coroutine_IsYieldable_InsideCoroutine()
    {
        var r = await CreateState().DoStringAsync(@"
            local co = coroutine.create(function() coroutine.yield(coroutine.isyieldable()) end)
            local ok, v = coroutine.resume(co)
            return v
        ");
        Assert.That(r[0].Read<bool>(), Is.True);
    }

    // --- string.pack / unpack / packsize ---------------------------------------------

    [Test]
    public async Task StringPack_Int32_LittleEndian()
    {
        var r = await CreateState().DoStringAsync(@"return string.pack(""<i4"", 0x01020304)");
        var s = r[0].Read<string>();
        Assert.That(s.Length, Is.EqualTo(4));
        Assert.That((int)s[0], Is.EqualTo(0x04));
        Assert.That((int)s[1], Is.EqualTo(0x03));
        Assert.That((int)s[2], Is.EqualTo(0x02));
        Assert.That((int)s[3], Is.EqualTo(0x01));
    }

    [Test]
    public async Task StringPack_Int32_BigEndian()
    {
        var r = await CreateState().DoStringAsync(@"return string.pack("">i4"", 0x01020304)");
        var s = r[0].Read<string>();
        Assert.That((int)s[0], Is.EqualTo(0x01));
        Assert.That((int)s[1], Is.EqualTo(0x02));
        Assert.That((int)s[2], Is.EqualTo(0x03));
        Assert.That((int)s[3], Is.EqualTo(0x04));
    }

    [Test]
    public async Task StringUnpack_RoundTrip_Int32()
    {
        var r = await CreateState().DoStringAsync(@"return string.unpack("">i4"", string.pack("">i4"", 42))");
        Assert.That(r[0].Read<long>(), Is.EqualTo(42L));
        Assert.That(r[1].Read<long>(), Is.EqualTo(5L)); // next position
    }

    [Test]
    public async Task StringPackSize_FixedSizes()
    {
        var r = await CreateState().DoStringAsync(@"return string.packsize(""i4"")");
        Assert.That(r[0].Read<long>(), Is.EqualTo(4L));
    }

    [Test]
    public void StringPackSize_VariableSize_Throws()
    {
        var s = CreateState();
        Assert.ThrowsAsync<LuaRuntimeException>(async () =>
            await s.DoStringAsync(@"return string.packsize(""s4"")").AsTask());
    }

    // --- Integer literal lex/parse classification ------------------------------------

    [TestCase("return math.type(0)", "integer")]
    [TestCase("return math.type(0.0)", "float")]
    [TestCase("return math.type(0xff)", "integer")]
    [TestCase("return math.type(0xff.0)", "float")]
    [TestCase("return math.type(1e2)", "float")]
    [TestCase("return math.type(0x1p4)", "float")]
    public async Task LiteralClassification(string code, string expected)
    {
        var r = await CreateState().DoStringAsync(code);
        Assert.That(r[0].Read<string>(), Is.EqualTo(expected));
    }

    [Test]
    public async Task UnaryMinusInteger_PreservesSubtype()
    {
        var r = await CreateState().DoStringAsync("return math.type(-1)");
        Assert.That(r[0].Read<string>(), Is.EqualTo("integer"));
    }

    // --- Strict 5.3: dropped math functions are not callable ------------------------

    [TestCase("math.atan2")]
    [TestCase("math.cosh")]
    [TestCase("math.sinh")]
    [TestCase("math.tanh")]
    [TestCase("math.frexp")]
    [TestCase("math.ldexp")]
    [TestCase("math.pow")]
    public async Task DeprecatedMathFunctions_AreRemoved(string name)
    {
        var r = await CreateState().DoStringAsync($"return type({name})");
        Assert.That(r[0].Read<string>(), Is.EqualTo("nil"),
            $"{name} must not be present in Lua 5.3 (it was removed).");
    }
}
