using System.Buffers.Binary;
using System.Text;
using CoCoach.Telemetry.Ibt;

namespace CoCoach.Tests.Support;

/// <summary>One variable to write into a synthetic .ibt file.</summary>
internal sealed record IbtVarSpec(string Name, IbtVarType Type, string Unit = "", int Count = 1, string Description = "");

/// <summary>
/// Writes a minimal but valid .ibt file in the irsdk disk layout (header, disk sub-header, var headers,
/// session-info YAML, fixed-length records) so the real SDK replay path can be tested without iRacing.
/// </summary>
internal static class IbtWriter
{
    const int HeaderSize = 112;
    const int DiskSubHeaderSize = 32;
    const int VarHeaderSize = 144;

    /// <param name="rows">One value per variable per row; array variables repeat the value in every element.</param>
    public static void Write(
        string path,
        IReadOnlyList<IbtVarSpec> vars,
        string yaml,
        IReadOnlyList<double[]> rows,
        int tickRate = 60,
        int version = 2,
        long sessionStartDate = 1_767_225_600, // 2026-01-01
        int lapCount = 0)
    {
        // lay variables out naturally aligned
        var offsets = new int[vars.Count];
        var bufLen = 0;
        for (var i = 0; i < vars.Count; i++)
        {
            var size = ElementSize(vars[i].Type);
            bufLen = Align(bufLen, size);
            offsets[i] = bufLen;
            bufLen += size * vars[i].Count;
        }
        bufLen = Align(bufLen, 16);

        var yamlBytes = Encoding.UTF8.GetBytes(yaml);
        var varHeaderOffset = HeaderSize + DiskSubHeaderSize;
        var sessionInfoOffset = varHeaderOffset + vars.Count * VarHeaderSize;
        var sessionInfoLen = Align(yamlBytes.Length + 1, 16);
        var firstRecordOffset = sessionInfoOffset + sessionInfoLen;

        var file = new byte[firstRecordOffset + (long)bufLen * rows.Count];
        var span = file.AsSpan();

        void I32(int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at), value);

        I32(0, version);
        I32(4, 1);                 // status: connected
        I32(8, tickRate);
        I32(12, 1);                // sessionInfoUpdate
        I32(16, sessionInfoLen);
        I32(20, sessionInfoOffset);
        I32(24, vars.Count);
        I32(28, varHeaderOffset);
        I32(32, 1);                // numBuf
        I32(36, bufLen);
        I32(48 + 4, firstRecordOffset); // varBuf[0].bufOffset

        var sub = HeaderSize;
        BinaryPrimitives.WriteInt64LittleEndian(span[sub..], sessionStartDate);
        BinaryPrimitives.WriteDoubleLittleEndian(span[(sub + 8)..], 0);
        BinaryPrimitives.WriteDoubleLittleEndian(span[(sub + 16)..], rows.Count / (double)tickRate);
        I32(sub + 24, lapCount);
        I32(sub + 28, rows.Count);

        for (var i = 0; i < vars.Count; i++)
        {
            var at = varHeaderOffset + i * VarHeaderSize;
            I32(at, (int)vars[i].Type);
            I32(at + 4, offsets[i]);
            I32(at + 8, vars[i].Count);
            span[at + 12] = 0;
            Ascii(span.Slice(at + 16, 32), vars[i].Name);
            Ascii(span.Slice(at + 48, 64), vars[i].Description);
            Ascii(span.Slice(at + 112, 32), vars[i].Unit);
        }

        yamlBytes.CopyTo(span[sessionInfoOffset..]);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = span.Slice(firstRecordOffset + r * bufLen, bufLen);
            for (var i = 0; i < vars.Count; i++)
            {
                for (var e = 0; e < vars[i].Count; e++)
                    WriteValue(row[(offsets[i] + e * ElementSize(vars[i].Type))..], vars[i].Type, rows[r][i]);
            }
        }

        File.WriteAllBytes(path, file);
    }

    static void WriteValue(Span<byte> at, IbtVarType type, double value)
    {
        switch (type)
        {
            case IbtVarType.Char:
            case IbtVarType.Bool:
                at[0] = (byte)value;
                break;
            case IbtVarType.Int:
            case IbtVarType.BitField:
                BinaryPrimitives.WriteInt32LittleEndian(at, (int)value);
                break;
            case IbtVarType.Float:
                BinaryPrimitives.WriteSingleLittleEndian(at, (float)value);
                break;
            case IbtVarType.Double:
                BinaryPrimitives.WriteDoubleLittleEndian(at, value);
                break;
        }
    }

    static void Ascii(Span<byte> target, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        bytes.AsSpan(0, Math.Min(bytes.Length, target.Length - 1)).CopyTo(target);
    }

    static int ElementSize(IbtVarType type) => type switch
    {
        IbtVarType.Char or IbtVarType.Bool => 1,
        IbtVarType.Double => 8,
        _ => 4,
    };

    static int Align(int value, int to) => (value + to - 1) / to * to;
}
