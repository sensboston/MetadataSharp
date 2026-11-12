using System.Buffers.Binary;
using System.Text;
using MetadataSharp.Core;
using Xunit;

namespace MetadataSharp.Tests.Values;

/// <summary>
/// Typed reading of a raw value: every field type in both byte orders, the field types each accessor
/// refuses, and the counts a single-value accessor refuses. Nothing here throws for a mismatch.
/// </summary>
public sealed class ExifValueAccessorTests
{
    private static readonly TiffFieldType[] AllTypes = Enum.GetValues<TiffFieldType>();

    /// <summary>The elements written one after another in the given byte order.</summary>
    private static byte[] Pack(Endianness order, int width, params ulong[] elements)
    {
        var bytes = new byte[elements.Length * width];
        for (int i = 0; i < elements.Length; i++)
        {
            Span<byte> at = bytes.AsSpan(i * width, width);
            bool little = order == Endianness.LittleEndian;
            switch (width)
            {
                case 1: at[0] = (byte)elements[i]; break;
                case 2:
                    if (little) BinaryPrimitives.WriteUInt16LittleEndian(at, (ushort)elements[i]);
                    else BinaryPrimitives.WriteUInt16BigEndian(at, (ushort)elements[i]);
                    break;
                case 4:
                    if (little) BinaryPrimitives.WriteUInt32LittleEndian(at, (uint)elements[i]);
                    else BinaryPrimitives.WriteUInt32BigEndian(at, (uint)elements[i]);
                    break;
                default:
                    if (little) BinaryPrimitives.WriteUInt64LittleEndian(at, elements[i]);
                    else BinaryPrimitives.WriteUInt64BigEndian(at, elements[i]);
                    break;
            }
        }
        return bytes;
    }

    private static int Width(TiffFieldType type) => type switch
    {
        TiffFieldType.Byte or TiffFieldType.Ascii or TiffFieldType.SByte or TiffFieldType.Undefined => 1,
        TiffFieldType.Short or TiffFieldType.SShort => 2,
        TiffFieldType.Long or TiffFieldType.SLong or TiffFieldType.Float => 4,
        _ => 8,
    };

    private static ExifValue Value(TiffFieldType type, Endianness order, params ulong[] elements) =>
        ExifValue.FromRaw(type, (uint)elements.Length, order, Pack(order, Width(type), elements));

    /// <summary>A rational is two 32-bit halves, each in the byte order, numerator first.</summary>
    private static ExifValue Pairs(TiffFieldType type, Endianness order, params (uint Numerator, uint Denominator)[] pairs)
    {
        var halves = new ulong[pairs.Length * 2];
        for (int i = 0; i < pairs.Length; i++)
        {
            halves[i * 2] = pairs[i].Numerator;
            halves[(i * 2) + 1] = pairs[i].Denominator;
        }
        return ExifValue.FromRaw(type, (uint)pairs.Length, order, Pack(order, 4, halves));
    }

    private static ExifValue Ascii(string text, Endianness order = Endianness.LittleEndian) => Ascii(Encoding.Latin1.GetBytes(text), order);

    private static ExifValue Ascii(byte[] bytes, Endianness order = Endianness.LittleEndian) =>
        ExifValue.FromRaw(TiffFieldType.Ascii, (uint)bytes.Length, order, bytes);

    // ---- Every field type, both byte orders ---------------------------------------------------------

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void ByteAndUndefinedReadAsBytes(Endianness order)
    {
        foreach (TiffFieldType type in new[] { TiffFieldType.Byte, TiffFieldType.Undefined })
        {
            ExifValue value = Value(type, order, 0, 1, 0x7F, 0x80, 0xFF);
            Assert.True(value.TryGetByteArray(out byte[]? bytes));
            Assert.Equal(new byte[] { 0, 1, 0x7F, 0x80, 0xFF }, bytes);
        }
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void ByteWidensToEveryUnsignedFormAndToInt64(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.Byte, order, 0, 0x80, 0xFF);
        Assert.True(value.TryGetUInt16Array(out ushort[]? shorts));
        Assert.Equal(new ushort[] { 0, 0x80, 0xFF }, shorts);
        Assert.True(value.TryGetUInt32Array(out uint[]? longs));
        Assert.Equal(new uint[] { 0, 0x80, 0xFF }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 0x80, 0xFF }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SByteReadsSignedAndWidens(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.SByte, order, 0, 0x7F, 0x80, 0xFF);
        Assert.True(value.TryGetSByteArray(out sbyte[]? bytes));
        Assert.Equal(new sbyte[] { 0, 127, -128, -1 }, bytes);
        Assert.True(value.TryGetInt16Array(out short[]? shorts));
        Assert.Equal(new short[] { 0, 127, -128, -1 }, shorts);
        Assert.True(value.TryGetInt32Array(out int[]? longs));
        Assert.Equal(new[] { 0, 127, -128, -1 }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 127, -128, -1 }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void ShortReadsInItsByteOrderAndWidens(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.Short, order, 0, 1, 0x0102, 0x8000, 0xFFFF);
        Assert.True(value.TryGetUInt16Array(out ushort[]? shorts));
        Assert.Equal(new ushort[] { 0, 1, 0x0102, 0x8000, 0xFFFF }, shorts);
        Assert.True(value.TryGetUInt32Array(out uint[]? longs));
        Assert.Equal(new uint[] { 0, 1, 0x0102, 0x8000, 0xFFFF }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 1, 0x0102, 0x8000, 0xFFFF }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SShortReadsSignedInItsByteOrderAndWidens(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.SShort, order, 0, 0x0102, 0x7FFF, 0x8000, 0xFFFF);
        Assert.True(value.TryGetInt16Array(out short[]? shorts));
        Assert.Equal(new short[] { 0, 0x0102, short.MaxValue, short.MinValue, -1 }, shorts);
        Assert.True(value.TryGetInt32Array(out int[]? longs));
        Assert.Equal(new[] { 0, 0x0102, short.MaxValue, short.MinValue, -1 }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 0x0102, short.MaxValue, short.MinValue, -1 }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void LongReadsInItsByteOrder(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.Long, order, 0, 0x01020304, 0x80000000, 0xFFFFFFFF);
        Assert.True(value.TryGetUInt32Array(out uint[]? longs));
        Assert.Equal(new uint[] { 0, 0x01020304, 0x80000000, uint.MaxValue }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 0x01020304, 0x80000000, uint.MaxValue }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SLongReadsSignedInItsByteOrder(Endianness order)
    {
        ExifValue value = Value(TiffFieldType.SLong, order, 0, 0x01020304, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF);
        Assert.True(value.TryGetInt32Array(out int[]? longs));
        Assert.Equal(new[] { 0, 0x01020304, int.MaxValue, int.MinValue, -1 }, longs);
        Assert.True(value.TryGetInt64Array(out long[]? wide));
        Assert.Equal(new long[] { 0, 0x01020304, int.MaxValue, int.MinValue, -1 }, wide);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void RationalKeepsEachPairAsStored(Endianness order)
    {
        ExifValue value = Pairs(TiffFieldType.Rational, order, (1, 250), (2, 4), (0, 0), (uint.MaxValue, 0x01020304));
        Assert.True(value.TryGetRationalArray(out UnsignedRational[]? pairs));
        Assert.Equal(
            new UnsignedRational[] { new(1, 250), new(2, 4), new(0, 0), new(uint.MaxValue, 0x01020304) },
            pairs);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SRationalKeepsEachSignedPairAsStored(Endianness order)
    {
        ExifValue value = Pairs(TiffFieldType.SRational, order,
            (unchecked((uint)-2), 3), (4, unchecked((uint)-6)), (0x80000000, 0x7FFFFFFF), (0, 0));
        Assert.True(value.TryGetSignedRationalArray(out SignedRational[]? pairs));
        Assert.Equal(
            new SignedRational[] { new(-2, 3), new(4, -6), new(int.MinValue, int.MaxValue), new(0, 0) },
            pairs);
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void FloatReadsBitForBitAndWidensToDouble(Endianness order)
    {
        float[] expected = [0f, 1.5f, -2.75f, float.MaxValue, float.Epsilon, float.NegativeInfinity, float.NaN];
        ExifValue value = Value(TiffFieldType.Float, order, expected.Select(f => (ulong)BitConverter.SingleToUInt32Bits(f)).ToArray());
        Assert.True(value.TryGetSingleArray(out float[]? singles));
        Assert.Equal(expected.Select(BitConverter.SingleToUInt32Bits).ToArray(), singles!.Select(BitConverter.SingleToUInt32Bits).ToArray());
        Assert.True(value.TryGetDoubleArray(out double[]? doubles));
        Assert.Equal(expected.Select(f => BitConverter.DoubleToUInt64Bits(f)).ToArray(), doubles!.Select(BitConverter.DoubleToUInt64Bits).ToArray());
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void DoubleReadsBitForBit(Endianness order)
    {
        double[] expected = [0d, 1.5, -2.75, Math.PI, double.MaxValue, double.Epsilon, double.PositiveInfinity, double.NaN];
        ExifValue value = Value(TiffFieldType.Double, order, expected.Select(BitConverter.DoubleToUInt64Bits).ToArray());
        Assert.True(value.TryGetDoubleArray(out double[]? doubles));
        Assert.Equal(expected.Select(BitConverter.DoubleToUInt64Bits).ToArray(), doubles!.Select(BitConverter.DoubleToUInt64Bits).ToArray());
    }

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void AsciiReadsTheSameInEitherByteOrder(Endianness order)
    {
        ExifValue value = Ascii("Canon EOS R6\0", order);
        Assert.True(value.TryGetString(out string? text));
        Assert.Equal("Canon EOS R6", text);
        Assert.True(value.TryGetStringArray(out string[]? strings));
        Assert.Equal(new[] { "Canon EOS R6" }, strings);
    }

    [Fact]
    public void TheByteOrderOfTheValueDecidesTheNumber()
    {
        byte[] raw = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08];
        foreach ((Endianness order, ushort first, uint firstLong, uint secondLong) in new[]
        {
            (Endianness.LittleEndian, (ushort)0x0201, 0x04030201U, 0x08070605U),
            (Endianness.BigEndian, (ushort)0x0102, 0x01020304U, 0x05060708U),
        })
        {
            Assert.True(ExifValue.FromRaw(TiffFieldType.Short, 4, order, raw).TryGetUInt16Array(out ushort[]? shorts));
            Assert.Equal(first, shorts![0]);
            Assert.True(ExifValue.FromRaw(TiffFieldType.Long, 2, order, raw).TryGetUInt32Array(out uint[]? longs));
            Assert.Equal(new[] { firstLong, secondLong }, longs);
            Assert.True(ExifValue.FromRaw(TiffFieldType.Rational, 1, order, raw).TryGetRational(out UnsignedRational pair));
            Assert.Equal(new UnsignedRational(firstLong, secondLong), pair);
            Assert.True(ExifValue.FromRaw(TiffFieldType.SRational, 1, order, raw).TryGetSignedRational(out SignedRational signed));
            Assert.Equal(new SignedRational((int)firstLong, (int)secondLong), signed);
            Assert.True(ExifValue.FromRaw(TiffFieldType.Double, 1, order, raw).TryGetDouble(out double number));
            Assert.Equal(((ulong)firstLong << (order == Endianness.BigEndian ? 32 : 0)) | ((ulong)secondLong << (order == Endianness.BigEndian ? 0 : 32)),
                BitConverter.DoubleToUInt64Bits(number));
        }
    }

    // ---- Single values --------------------------------------------------------------------------------

    [Theory]
    [InlineData(Endianness.LittleEndian)]
    [InlineData(Endianness.BigEndian)]
    public void SingleValueFormsReadTheOneElement(Endianness order)
    {
        Assert.True(Value(TiffFieldType.Byte, order, 200).TryGetUInt32(out uint number));
        Assert.Equal(200U, number);
        Assert.True(Value(TiffFieldType.Short, order, 0xFFEE).TryGetUInt32(out number));
        Assert.Equal(0xFFEEU, number);
        Assert.True(Value(TiffFieldType.Long, order, 0xFFEEDDCC).TryGetUInt32(out number));
        Assert.Equal(0xFFEEDDCCU, number);

        Assert.True(Value(TiffFieldType.SByte, order, 0x80).TryGetInt32(out int signed));
        Assert.Equal(-128, signed);
        Assert.True(Value(TiffFieldType.SShort, order, 0xFFFE).TryGetInt32(out signed));
        Assert.Equal(-2, signed);
        Assert.True(Value(TiffFieldType.SLong, order, 0x80000001).TryGetInt32(out signed));
        Assert.Equal(int.MinValue + 1, signed);

        (TiffFieldType Type, ulong Stored, long Expected)[] integers =
        [
            (TiffFieldType.Byte, 0xFF, 255), (TiffFieldType.Short, 0xFFFF, 65535), (TiffFieldType.Long, 0xFFFFFFFF, uint.MaxValue),
            (TiffFieldType.SByte, 0xFF, -1), (TiffFieldType.SShort, 0xFFFF, -1), (TiffFieldType.SLong, 0xFFFFFFFF, -1),
        ];
        foreach ((TiffFieldType type, ulong stored, long expected) in integers)
        {
            Assert.True(Value(type, order, stored).TryGetInt64(out long wide));
            Assert.Equal(expected, wide);
        }

        Assert.True(Pairs(TiffFieldType.Rational, order, (28, 10)).TryGetRational(out UnsignedRational pair));
        Assert.Equal(new UnsignedRational(28, 10), pair);
        Assert.True(Pairs(TiffFieldType.SRational, order, (unchecked((uint)-1), 3)).TryGetSignedRational(out SignedRational signedPair));
        Assert.Equal(new SignedRational(-1, 3), signedPair);

        Assert.True(Value(TiffFieldType.Float, order, BitConverter.SingleToUInt32Bits(0.25f)).TryGetDouble(out double real));
        Assert.Equal(0.25, real);
        Assert.True(Value(TiffFieldType.Double, order, BitConverter.DoubleToUInt64Bits(-1.0 / 3)).TryGetDouble(out real));
        Assert.Equal(-1.0 / 3, real);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void SingleValueFormsRefuseEveryCountButOne(int count)
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            foreach (TiffFieldType type in AllTypes)
            {
                ExifValue value = ExifValue.FromRaw(type, (uint)count, order, new byte[count * Width(type)]);
                Assert.False(value.TryGetUInt32(out uint number));
                Assert.Equal(0U, number);
                Assert.False(value.TryGetInt32(out int signed));
                Assert.Equal(0, signed);
                Assert.False(value.TryGetInt64(out long wide));
                Assert.Equal(0L, wide);
                Assert.False(value.TryGetRational(out UnsignedRational pair));
                Assert.Equal(default, pair);
                Assert.False(value.TryGetSignedRational(out SignedRational signedPair));
                Assert.Equal(default, signedPair);
                Assert.False(value.TryGetDouble(out double real));
                Assert.Equal(0d, real);
            }
        }
    }

    // ---- Mismatches -----------------------------------------------------------------------------------

    /// <summary>The field types each accessor reads; every other type has to be refused.</summary>
    private static readonly (string Accessor, TiffFieldType[] Reads)[] Accepted =
    [
        ("ByteArray", [TiffFieldType.Byte, TiffFieldType.Undefined]),
        ("SByteArray", [TiffFieldType.SByte]),
        ("UInt16Array", [TiffFieldType.Byte, TiffFieldType.Short]),
        ("UInt32Array", [TiffFieldType.Byte, TiffFieldType.Short, TiffFieldType.Long]),
        ("Int16Array", [TiffFieldType.SByte, TiffFieldType.SShort]),
        ("Int32Array", [TiffFieldType.SByte, TiffFieldType.SShort, TiffFieldType.SLong]),
        ("Int64Array", [TiffFieldType.Byte, TiffFieldType.Short, TiffFieldType.Long, TiffFieldType.SByte, TiffFieldType.SShort, TiffFieldType.SLong]),
        ("RationalArray", [TiffFieldType.Rational]),
        ("SignedRationalArray", [TiffFieldType.SRational]),
        ("SingleArray", [TiffFieldType.Float]),
        ("DoubleArray", [TiffFieldType.Float, TiffFieldType.Double]),
        ("String", [TiffFieldType.Ascii]),
        ("StringArray", [TiffFieldType.Ascii]),
        ("UInt32", [TiffFieldType.Byte, TiffFieldType.Short, TiffFieldType.Long]),
        ("Int32", [TiffFieldType.SByte, TiffFieldType.SShort, TiffFieldType.SLong]),
        ("Int64", [TiffFieldType.Byte, TiffFieldType.Short, TiffFieldType.Long, TiffFieldType.SByte, TiffFieldType.SShort, TiffFieldType.SLong]),
        ("Rational", [TiffFieldType.Rational]),
        ("SignedRational", [TiffFieldType.SRational]),
        ("Double", [TiffFieldType.Float, TiffFieldType.Double]),
    ];

    /// <summary>Calls one accessor by name; the second item is what it left in its out parameter.</summary>
    private static (bool Read, object? Result) Call(string accessor, ExifValue value)
    {
        switch (accessor)
        {
            case "ByteArray": { bool read = value.TryGetByteArray(out byte[]? result); return (read, result); }
            case "SByteArray": { bool read = value.TryGetSByteArray(out sbyte[]? result); return (read, result); }
            case "UInt16Array": { bool read = value.TryGetUInt16Array(out ushort[]? result); return (read, result); }
            case "UInt32Array": { bool read = value.TryGetUInt32Array(out uint[]? result); return (read, result); }
            case "Int16Array": { bool read = value.TryGetInt16Array(out short[]? result); return (read, result); }
            case "Int32Array": { bool read = value.TryGetInt32Array(out int[]? result); return (read, result); }
            case "Int64Array": { bool read = value.TryGetInt64Array(out long[]? result); return (read, result); }
            case "RationalArray": { bool read = value.TryGetRationalArray(out UnsignedRational[]? result); return (read, result); }
            case "SignedRationalArray": { bool read = value.TryGetSignedRationalArray(out SignedRational[]? result); return (read, result); }
            case "SingleArray": { bool read = value.TryGetSingleArray(out float[]? result); return (read, result); }
            case "DoubleArray": { bool read = value.TryGetDoubleArray(out double[]? result); return (read, result); }
            case "String": { bool read = value.TryGetString(out string? result); return (read, result); }
            case "StringArray": { bool read = value.TryGetStringArray(out string[]? result); return (read, result); }
            case "UInt32": { bool read = value.TryGetUInt32(out uint result); return (read, read ? result : result == 0 ? null : result); }
            case "Int32": { bool read = value.TryGetInt32(out int result); return (read, read ? result : result == 0 ? null : result); }
            case "Int64": { bool read = value.TryGetInt64(out long result); return (read, read ? result : result == 0 ? null : result); }
            case "Rational": { bool read = value.TryGetRational(out UnsignedRational result); return (read, read ? result : result == default ? null : result); }
            case "SignedRational": { bool read = value.TryGetSignedRational(out SignedRational result); return (read, read ? result : result == default ? null : result); }
            case "Double": { bool read = value.TryGetDouble(out double result); return (read, read ? result : result == 0 ? null : result); }
            default: throw new ArgumentOutOfRangeException(nameof(accessor));
        }
    }

    [Fact]
    public void EveryAccessorReadsItsFieldTypesAndRefusesTheRestWithoutThrowing()
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            foreach (TiffFieldType type in AllTypes)
            {
                // One element of all-ones bits: a value every type can hold.
                byte[] raw = Enumerable.Repeat((byte)0xFF, Width(type)).ToArray();
                ExifValue value = ExifValue.FromRaw(type, 1, order, raw);
                foreach ((string accessor, TiffFieldType[] reads) in Accepted)
                {
                    (bool read, object? result) = Call(accessor, value);
                    Assert.True(read == reads.Contains(type), $"{accessor} on {type} ({order})");
                    // A refusal leaves nothing behind: null for a reference, the default for a number.
                    Assert.True(read ? result is not null : result is null, $"{accessor} on {type} ({order}) left {result}");
                }
            }
        }
    }

    [Fact]
    public void TheMismatchTableNamesEveryPublicAccessor()
    {
        string[] declared = typeof(ExifValue).GetMethods()
            .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal))
            .Select(m => m.Name["TryGet".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(Accepted.Select(a => a.Accessor).Order(StringComparer.Ordinal).ToArray(), declared);
    }

    // ---- Counts ---------------------------------------------------------------------------------------

    [Fact]
    public void ACountOfZeroIsAnEmptyArrayOfTheRightTypeAndNotARefusal()
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            foreach (TiffFieldType type in AllTypes)
            {
                ExifValue value = ExifValue.FromRaw(type, 0, order, []);
                foreach ((string accessor, TiffFieldType[] reads) in Accepted)
                {
                    if (!accessor.EndsWith("Array", StringComparison.Ordinal) && accessor != "String")
                        continue;
                    (bool read, object? result) = Call(accessor, value);
                    Assert.Equal(reads.Contains(type), read);
                    if (!read)
                        Assert.Null(result);
                    else if (result is string text)
                        Assert.Equal(string.Empty, text);
                    else
                        Assert.Empty((Array)result!);
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(1000)]
    public void AnArrayHasExactlyCountElements(int count)
    {
        foreach (Endianness order in Enum.GetValues<Endianness>())
        {
            foreach (TiffFieldType type in AllTypes)
            {
                ExifValue value = ExifValue.FromRaw(type, (uint)count, order, new byte[count * Width(type)]);
                foreach ((string accessor, TiffFieldType[] reads) in Accepted)
                {
                    if (!accessor.EndsWith("Array", StringComparison.Ordinal) || accessor == "StringArray" || !reads.Contains(type))
                        continue;
                    (bool read, object? result) = Call(accessor, value);
                    Assert.True(read);
                    Assert.Equal(count, ((Array)result!).Length);
                }
            }
        }
    }

    [Fact]
    public void EachCallReturnsAnArrayOfItsOwn()
    {
        ExifValue value = Value(TiffFieldType.Short, Endianness.BigEndian, 1, 2, 3);
        Assert.True(value.TryGetUInt16Array(out ushort[]? first));
        first![0] = 99;
        Assert.True(value.TryGetUInt16Array(out ushort[]? second));
        Assert.NotSame(first, second);
        Assert.Equal(new ushort[] { 1, 2, 3 }, second);

        ExifValue bytes = Value(TiffFieldType.Undefined, Endianness.BigEndian, 1, 2, 3);
        Assert.True(bytes.TryGetByteArray(out byte[]? raw));
        raw![0] = 99;
        byte[] kept = new byte[3];
        bytes.CopyRawTo(kept);
        Assert.Equal(new byte[] { 1, 2, 3 }, kept);
    }

    // ---- Text -----------------------------------------------------------------------------------------

    // In the rows below a bar stands for a NUL byte, which a test name cannot carry.
    [Theory]
    [InlineData("Canon|", "Canon")]
    [InlineData("Canon", "Canon")]                       // no terminator: the whole value
    [InlineData("Canon||||", "Canon")]               // padding
    [InlineData("Canon|junk|", "Canon")]               // anything after the terminator is left out
    [InlineData("  padded  |", "  padded  ")]           // spaces are content
    [InlineData("|", "")]
    [InlineData("|||", "")]
    [InlineData("|late|", "")]
    [InlineData("", "")]
    public void AStringEndsAtTheFirstNul(string stored, string expected)
    {
        Assert.True(Ascii(stored.Replace('|', '\0')).TryGetString(out string? text));
        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData("Canon|", new[] { "Canon" })]
    [InlineData("Canon", new[] { "Canon" })]
    [InlineData("Canon||||", new[] { "Canon" })]
    [InlineData("photographer|editor|", new[] { "photographer", "editor" })]
    [InlineData("photographer|editor", new[] { "photographer", "editor" })]
    [InlineData("a||c|", new[] { "a", "", "c" })]     // an empty string between two others is kept
    [InlineData("|editor|", new[] { "", "editor" })]
    [InlineData("|", new string[0])]
    [InlineData("|||", new string[0])]
    [InlineData("", new string[0])]
    public void EveryStringOfAValueIsListedWithoutItsTerminator(string stored, string[] expected)
    {
        Assert.True(Ascii(stored.Replace('|', '\0')).TryGetStringArray(out string[]? strings));
        Assert.Equal(expected, strings);
    }

    [Fact]
    public void BytesAboveAsciiAreUtf8WhenValidAndLatin1Otherwise()
    {
        // "Zoë" in UTF-8, then the same word in ISO 8859-1, which is not valid UTF-8.
        Assert.True(Ascii([0x5A, 0x6F, 0xC3, 0xAB, 0x00]).TryGetString(out string? utf8));
        Assert.Equal("Zo\u00EB", utf8);
        Assert.True(Ascii([0x5A, 0x6F, 0xEB, 0x00]).TryGetString(out string? latin1));
        Assert.Equal("Zo\u00EB", latin1);

        // Each string of a value is decoded on its own.
        Assert.True(Ascii([0x5A, 0x6F, 0xC3, 0xAB, 0x00, 0x5A, 0x6F, 0xEB, 0x00, 0xE6, 0x97, 0xA5, 0x00]).TryGetStringArray(out string[]? strings));
        Assert.Equal(new[] { "Zo\u00EB", "Zo\u00EB", "\u65E5" }, strings);

        // Every byte value has a reading: nothing is refused or replaced.
        byte[] every = Enumerable.Range(1, 255).Select(i => (byte)i).ToArray();
        Assert.True(Ascii(every).TryGetString(out string? all));
        Assert.Equal(255, all!.Length);
        Assert.Equal(every.Select(b => (char)b).ToArray(), all.ToCharArray());
    }

    // ---- The rational types ---------------------------------------------------------------------------

    [Fact]
    public void ARationalPrintsAsWrittenAndDividesInDoublePrecision()
    {
        Assert.Equal("1/250", new UnsignedRational(1, 250).ToString());
        Assert.Equal("4294967295/1", new UnsignedRational(uint.MaxValue, 1).ToString());
        Assert.Equal("2/4", new UnsignedRational(2, 4).ToString());
        Assert.Equal("0/0", default(UnsignedRational).ToString());
        Assert.Equal("-2/3", new SignedRational(-2, 3).ToString());
        Assert.Equal("4/-6", new SignedRational(4, -6).ToString());
        Assert.Equal("-2147483648/2147483647", new SignedRational(int.MinValue, int.MaxValue).ToString());
        Assert.Equal("1/250", $"{new UnsignedRational(1, 250)}");

        Assert.Equal(0.004, new UnsignedRational(1, 250).ToDouble());
        Assert.Equal(2.8, new UnsignedRational(28, 10).ToDouble());
        Assert.Equal(4294967295.0, new UnsignedRational(uint.MaxValue, 1).ToDouble());
        Assert.Equal(-2.0 / 3, new SignedRational(-2, 3).ToDouble());
        Assert.Equal(2147483648.0, new SignedRational(int.MinValue, -1).ToDouble());

        // A zero denominator is not refused here: the division says what it says.
        Assert.Equal(double.PositiveInfinity, new UnsignedRational(1, 0).ToDouble());
        Assert.Equal(double.NegativeInfinity, new SignedRational(-1, 0).ToDouble());
        Assert.True(double.IsNaN(default(UnsignedRational).ToDouble()));
        Assert.True(double.IsNaN(default(SignedRational).ToDouble()));
    }

    [Fact]
    public void ARationalPrintsTheSameUnderAnyCulture()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not the ASCII hyphen.
            var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            culture.NumberFormat.NegativeSign = "\u2212";
            System.Globalization.CultureInfo.CurrentCulture = culture;
            Assert.Equal("-2/3", new SignedRational(-2, 3).ToString());
            Assert.Equal("1/250", new UnsignedRational(1, 250).ToString());
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }
}
