using System.Buffers.Binary;
using System.Text;

namespace CoCoach.Telemetry.Ibt;

/// <summary>irsdk variable types as stored in var headers.</summary>
public enum IbtVarType
{
    Char = 0,
    Bool = 1,
    Int = 2,
    BitField = 3,
    Float = 4,
    Double = 5,
}

/// <summary>One variable header from an .ibt file.</summary>
public sealed record IbtVariable(
    string Name,
    IbtVarType Type,
    int Offset,
    int Count,
    bool CountAsTime,
    string Unit,
    string Description)
{
    /// <summary>Size of one element in bytes.</summary>
    public int ElementSize => Type switch
    {
        IbtVarType.Char or IbtVarType.Bool => 1,
        IbtVarType.Double => 8,
        _ => 4,
    };

    public string TypeName => Type switch
    {
        IbtVarType.Char => "char",
        IbtVarType.Bool => "bool",
        IbtVarType.Int => "int",
        IbtVarType.BitField => "bitfield",
        IbtVarType.Float => "float",
        IbtVarType.Double => "double",
        _ => Type.ToString(),
    };
}

/// <summary>Header facts of an .ibt file.</summary>
public sealed record IbtHeader
{
    public required int Version { get; init; }
    public required int TickRate { get; init; }
    public required int SessionInfoUpdate { get; init; }
    public required int SessionInfoOffset { get; init; }
    public required int SessionInfoLength { get; init; }
    public required int VarCount { get; init; }
    public required int VarHeaderOffset { get; init; }
    public required int BufferCount { get; init; }
    public required int RecordLength { get; init; }
    public required int FirstRecordOffset { get; init; }

    /// <summary>Session start, from the disk sub-header (seconds since the Unix epoch).</summary>
    public required long SessionStartDate { get; init; }

    /// <summary>Seconds since <see cref="SessionStartDate"/> when recording started.</summary>
    public required double SessionStartTime { get; init; }

    /// <summary>Seconds since <see cref="SessionStartDate"/> when recording ended.</summary>
    public required double SessionEndTime { get; init; }

    public required int SessionLapCount { get; init; }

    /// <summary>Record count the file claims in its disk sub-header.</summary>
    public required int ClaimedRecordCount { get; init; }

    /// <summary>Records actually present (fewer than claimed when the file is truncated).</summary>
    public required int RecordCount { get; init; }

    /// <summary>Recorded duration: records divided by tick rate.</summary>
    public TimeSpan Duration => TickRate > 0 ? TimeSpan.FromSeconds((double)RecordCount / TickRate) : TimeSpan.Zero;
}

/// <summary>
/// Minimal read-only reader for the .ibt layout (irsdk_header, irsdk_diskSubHeader, var headers,
/// session-info YAML, then fixed-length records). Replay goes through the SDK; this exists for the
/// facts the SDK does not expose (tick rate, record count) and for scanning a variable's values.
/// </summary>
public sealed class IbtFile : IDisposable
{
    internal const int HeaderSize = 112;
    internal const int DiskSubHeaderSize = 32;
    internal const int VarHeaderSize = 144;
    internal const int MaxString = 32;
    internal const int MaxDesc = 64;

    readonly FileStream _stream;
    readonly Dictionary<string, IbtVariable> _byName;

    IbtFile(FileStream stream, IbtHeader header, IReadOnlyList<IbtVariable> variables)
    {
        _stream = stream;
        Header = header;
        Variables = variables;
        _byName = new Dictionary<string, IbtVariable>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in variables)
            _byName.TryAdd(v.Name, v);
    }

    public IbtHeader Header { get; }

    /// <summary>Variables in file order.</summary>
    public IReadOnlyList<IbtVariable> Variables { get; }

    public IbtVariable? Find(string name) => _byName.GetValueOrDefault(name);

    public static IbtFile Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        try
        {
            var (header, variables) = ReadLayout(stream);
            return new IbtFile(stream, header, variables);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The session-info YAML. It names every driver: never log or transmit it.</summary>
    public string ReadSessionInfoYaml()
    {
        var bytes = new byte[Header.SessionInfoLength];
        _stream.Position = Header.SessionInfoOffset;
        _stream.ReadExactly(bytes);
        var end = Array.IndexOf(bytes, (byte)0);
        var span = end >= 0 ? bytes.AsSpan(0, end) : bytes.AsSpan();
        var encoding = IsUtf8(span) ? Encoding.UTF8 : Encoding.Latin1;
        return encoding.GetString(span);
    }

    /// <summary>
    /// Reads every record once and hands each requested variable's first element to <paramref name="onValue"/>
    /// as a double (bools and chars as 0/1). Arrays (such as 360 Hz <c>_ST</c> sub-tick channels) report every element.
    /// </summary>
    public void Scan(IReadOnlyList<IbtVariable> variables, Action<IbtVariable, double> onValue)
    {
        ArgumentNullException.ThrowIfNull(variables);
        ArgumentNullException.ThrowIfNull(onValue);
        if (variables.Count == 0)
            return;

        var row = new byte[Header.RecordLength];
        _stream.Position = Header.FirstRecordOffset;
        for (var r = 0; r < Header.RecordCount; r++)
        {
            _stream.ReadExactly(row);
            foreach (var v in variables)
            {
                for (var i = 0; i < v.Count; i++)
                    onValue(v, ReadElement(row, v, i));
            }
        }
    }

    internal static double ReadElement(ReadOnlySpan<byte> row, IbtVariable v, int element)
    {
        var at = v.Offset + element * v.ElementSize;
        return v.Type switch
        {
            IbtVarType.Char or IbtVarType.Bool => row[at],
            IbtVarType.Int or IbtVarType.BitField => BinaryPrimitives.ReadInt32LittleEndian(row[at..]),
            IbtVarType.Float => BinaryPrimitives.ReadSingleLittleEndian(row[at..]),
            IbtVarType.Double => BinaryPrimitives.ReadDoubleLittleEndian(row[at..]),
            _ => double.NaN,
        };
    }

    static (IbtHeader, IbtVariable[]) ReadLayout(FileStream stream)
    {
        var fileLength = stream.Length;
        if (fileLength < HeaderSize + DiskSubHeaderSize)
            throw new InvalidDataException($"File is {fileLength} bytes, too small for an .ibt header.");

        var head = new byte[HeaderSize + DiskSubHeaderSize];
        stream.Position = 0;
        stream.ReadExactly(head);

        int I(int offset) => BinaryPrimitives.ReadInt32LittleEndian(head.AsSpan(offset));

        var version = I(0);
        var tickRate = I(8);
        var sessionInfoUpdate = I(12);
        var sessionInfoLength = I(16);
        var sessionInfoOffset = I(20);
        var numVars = I(24);
        var varHeaderOffset = I(28);
        var numBuf = I(32);
        var bufLen = I(36);
        // varBuf[0] starts at 48: tickCount, bufOffset, tickCountBegin, pad
        var firstRecordOffset = I(48 + 4);

        var sub = head.AsSpan(HeaderSize);
        var startDate = BinaryPrimitives.ReadInt64LittleEndian(sub);
        var startTime = BinaryPrimitives.ReadDoubleLittleEndian(sub[8..]);
        var endTime = BinaryPrimitives.ReadDoubleLittleEndian(sub[16..]);
        var lapCount = BinaryPrimitives.ReadInt32LittleEndian(sub[24..]);
        var claimedRecords = BinaryPrimitives.ReadInt32LittleEndian(sub[28..]);

        if (version is < 1 or > 3)
            throw new InvalidDataException($"Header version {version} is not an irsdk version.");
        if (numVars <= 0 || bufLen <= 0)
            throw new InvalidDataException($"Invalid layout: numVars={numVars}, bufLen={bufLen}.");
        if (varHeaderOffset < HeaderSize || varHeaderOffset + (long)numVars * VarHeaderSize > fileLength)
            throw new InvalidDataException("Variable headers run past the end of the file.");
        if (sessionInfoOffset < HeaderSize || sessionInfoLength < 0 || sessionInfoOffset + (long)sessionInfoLength > fileLength)
            throw new InvalidDataException("Session info runs past the end of the file.");
        if (firstRecordOffset < HeaderSize || firstRecordOffset > fileLength)
            throw new InvalidDataException("First record offset is outside the file.");

        var available = (int)Math.Min((fileLength - firstRecordOffset) / bufLen, int.MaxValue);
        var records = claimedRecords >= 0 ? Math.Min(claimedRecords, available) : available;

        var varBytes = new byte[numVars * VarHeaderSize];
        stream.Position = varHeaderOffset;
        stream.ReadExactly(varBytes);

        var variables = new IbtVariable[numVars];
        for (var i = 0; i < numVars; i++)
        {
            var vh = varBytes.AsSpan(i * VarHeaderSize, VarHeaderSize);
            variables[i] = new IbtVariable(
                Name: CString(vh.Slice(16, MaxString)),
                Type: (IbtVarType)BinaryPrimitives.ReadInt32LittleEndian(vh),
                Offset: BinaryPrimitives.ReadInt32LittleEndian(vh[4..]),
                Count: BinaryPrimitives.ReadInt32LittleEndian(vh[8..]),
                CountAsTime: vh[12] != 0,
                Unit: CString(vh.Slice(16 + MaxString + MaxDesc, MaxString)),
                Description: CString(vh.Slice(16 + MaxString, MaxDesc)));

            var v = variables[i];
            if (v.Count < 1 || v.Offset < 0 || v.Offset + (long)v.Count * v.ElementSize > bufLen)
                throw new InvalidDataException($"Variable '{v.Name}' lies outside the {bufLen}-byte record.");
        }

        var header = new IbtHeader
        {
            Version = version,
            TickRate = tickRate,
            SessionInfoUpdate = sessionInfoUpdate,
            SessionInfoOffset = sessionInfoOffset,
            SessionInfoLength = sessionInfoLength,
            VarCount = numVars,
            VarHeaderOffset = varHeaderOffset,
            BufferCount = numBuf,
            RecordLength = bufLen,
            FirstRecordOffset = firstRecordOffset,
            SessionStartDate = startDate,
            SessionStartTime = startTime,
            SessionEndTime = endTime,
            SessionLapCount = lapCount,
            ClaimedRecordCount = claimedRecords,
            RecordCount = records,
        };
        return (header, variables);
    }

    static string CString(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end >= 0 ? bytes[..end] : bytes);
    }

    static bool IsUtf8(ReadOnlySpan<byte> yaml)
    {
        var key = "Encoding:"u8;
        var at = yaml.IndexOf(key);
        if (at < 0)
            return false;
        var rest = yaml[(at + key.Length)..];
        var eol = rest.IndexOf((byte)'\n');
        if (eol >= 0)
            rest = rest[..eol];
        return rest.IndexOf("UTF8"u8) >= 0;
    }

    public void Dispose() => _stream.Dispose();
}
