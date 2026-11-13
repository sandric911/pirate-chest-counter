using System.Buffers.Binary;
using FortniteReplayReader.Models;
using PirateChest.Core.Trace;
using Unreal.Core.Exceptions;
using Unreal.Core.Models.Enums;

namespace PirateChest.ReplayReader;

public sealed class ReplayPeek
{
    public bool Valid { get; init; }
    public bool IsLive { get; init; }
    public string? ReplayId { get; init; }
    public string? Branch { get; init; }
    public uint LengthMs { get; init; }
    public DateTime TimestampUtc { get; init; }
    public string? Error { get; init; }
}

/// <summary>What the first bytes of a replay file say about it.</summary>
public enum ReplayHeaderState
{
    /// <summary>The header could not be read (file being created, not a replay, …).</summary>
    Unreadable,
    /// <summary>Fortnite is still recording: the header is marked live, or the decryption key is not written yet.</summary>
    Recording,
    /// <summary>Fortnite has finished the replay: not live, and the decryption key is in the header.</summary>
    Finished,
}

/// <summary>
/// What the bytes of a replay file say about how far Fortnite got with it. A replay is a short info block (length,
/// live flag, decryption key) followed by chunks: the match header, match data, checkpoints, events.
/// </summary>
public sealed class ReplayLayout
{
    public ReplayHeaderState State { get; init; }
    public string? ReplayId { get; init; }
    public string? Branch { get; init; }
    /// <summary>Length of the match as the info block states it.</summary>
    public uint LengthMs { get; init; }
    public int DataChunks { get; init; }
    /// <summary>Where the last match data chunk ends. Null for replays older than chunk times (2018).</summary>
    public uint? DataEndMs { get; init; }
    /// <summary>Why the replay is not complete; null when it is.</summary>
    public string? Problem { get; init; }

    /// <summary>
    /// Fortnite has finished the replay and every byte of it is here: the header is final, the chunks follow one
    /// another exactly to the end of the file, and the match data reaches the length the header states. Fortnite
    /// writes the last data chunk with that very length and only then the final header, so a complete replay can be
    /// counted at once and will not come out short.
    /// </summary>
    public bool Complete => Problem == null;
}

/// <summary>
/// File access for replays. Replays are only ever opened for reading with FileShare.ReadWrite|Delete,
/// so Fortnite can keep writing, rename or delete them. Nothing is ever written, moved or copied to disk.
/// </summary>
public static class ReplayFiles
{
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

    /// <summary>
    /// The whole file as it is right now, in one read. A replay that was just finished is checked
    /// (<see cref="Inspect"/>) and counted (<see cref="Parse(byte[])"/>) from these very bytes, so nothing Fortnite does
    /// to the file in between can come between the check and the count.
    /// </summary>
    public static byte[] ReadAll(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var length = fs.Length;
        if (length > int.MaxValue)
        {
            throw new InvalidReplayException("Replay file is larger than 2 GB");
        }
        var bytes = GC.AllocateUninitializedArray<byte>((int)length);
        fs.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Walks the chunks of a replay without decoding them. Never throws.</summary>
    public static ReplayLayout Inspect(byte[] bytes) => HeaderPeekReader.Inspect(bytes);

    /// <summary>Reads only the info + header chunk (first few KB): replay id, live flag, build.</summary>
    public static ReplayPeek Peek(string path)
    {
        try
        {
            using var fs = OpenShared(path);
            var len = (int)Math.Min(fs.Length, 512 * 1024);
            var buf = new byte[len];
            fs.ReadExactly(buf, 0, len);
            return HeaderPeekReader.Peek(buf);
        }
        catch (Exception ex)
        {
            return new ReplayPeek { Valid = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Tells whether Fortnite has finished a replay, from the first few KB of the file. While a match is recorded the
    /// header is marked live and the decryption key is a run of zeros; the last thing Fortnite does is to clear the
    /// flag and write the real key, so a finished header means the replay can be counted right away.
    /// </summary>
    public static ReplayHeaderState Probe(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            var buf = new byte[ProbeBytes];
            var read = fs.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            return ProbeHeader(buf.AsSpan(0, read).ToArray());
        }
        catch
        {
            return ReplayHeaderState.Unreadable;
        }
    }

    /// <summary>Enough for the replay info block (magic, versions, 256-character name, flags, key) with a wide margin.</summary>
    private const int ProbeBytes = 4096;

    /// <summary><see cref="Probe"/> on bytes already read from the start of a replay file.</summary>
    public static ReplayHeaderState ProbeHeader(byte[] start) => HeaderPeekReader.Probe(start);

    /// <summary>Full parse into a parser-independent trace.</summary>
    public static MatchTrace Parse(string path) => Parse(ReadAll(path));

    /// <summary>
    /// When true, every player pawn field upstream models is decoded and pawns go through upstream's own bookkeeping,
    /// as before <see cref="PawnGroups"/>. The trace is the same either way, only slower to get; the program turns
    /// this on by itself if <see cref="SameWithAllPawnFields"/> ever says otherwise.
    /// </summary>
    public static bool ReadAllPawnFields { get; set; }

    /// <summary>Full parse of a replay already read with <see cref="ReadAll"/>.</summary>
    public static MatchTrace Parse(byte[] bytes) => Parse(bytes, ReadAllPawnFields);

    /// <summary>
    /// Parses a replay both ways, with only the pawn fields the counting uses and with all of them, and tells whether
    /// the two traces are the same to the last byte. Run on one of the player's own replays every time the program
    /// starts: skipping fields must never change what is counted, and this is the proof on the current Fortnite build.
    /// </summary>
    public static bool SameWithAllPawnFields(byte[] bytes)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };
        var lean = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Parse(bytes, readAllPawnFields: false), options);
        var full = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Parse(bytes, readAllPawnFields: true), options);
        return lean.AsSpan().SequenceEqual(full);
    }

    private static MatchTrace Parse(byte[] bytes, bool readAllPawnFields)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var reader = new ChestReplayReader { ReadAllPawnFields = readAllPawnFields };
        var replay = reader.ReadReplay(stream);
        var trace = reader.BuildTrace(replay);
        if (string.IsNullOrEmpty(trace.Meta.ReplayId))
        {
            throw new InvalidReplayException("Replay header has no id");
        }
        return trace;
    }

    private sealed class HeaderPeekReader : FortniteReplayReader.ReplayReader
    {
        private HeaderPeekReader() : base(null!, Unreal.Core.Models.Enums.ParseMode.EventsOnly) { }

        public static ReplayHeaderState Probe(byte[] bytes) => Probe(bytes, out _, out _);

        /// <param name="chunksAt">Where the info block ends and the chunks begin.</param>
        private static ReplayHeaderState Probe(byte[] bytes, out HeaderPeekReader r, out int chunksAt)
        {
            r = new HeaderPeekReader { Replay = new FortniteReplay() };
            chunksAt = 0;
            try
            {
                using var archive = new Unreal.Core.BinaryReader(bytes.AsMemory());
                r.ReadReplayInfo(archive);
                chunksAt = archive.Position;
            }
            catch (InvalidReplayException ex) when (ex.Message.Contains("not yet marked as completed", StringComparison.OrdinalIgnoreCase))
            {
                return ReplayHeaderState.Recording;
            }
            catch
            {
                return ReplayHeaderState.Unreadable;
            }

            var info = r.Replay.Info;
            if (info.IsLive)
            {
                return ReplayHeaderState.Recording;
            }
            // Not live but the key is still the placeholder: the header is being rewritten right now.
            if (info.IsEncrypted && !info.EncryptionKey.Span.ContainsAnyExcept((byte)0))
            {
                return ReplayHeaderState.Recording;
            }
            return ReplayHeaderState.Finished;
        }

        public static ReplayLayout Inspect(byte[] bytes)
        {
            var state = Probe(bytes, out var r, out var chunksAt);
            if (state != ReplayHeaderState.Finished)
            {
                return new ReplayLayout
                {
                    State = state,
                    Problem = state == ReplayHeaderState.Recording ? "Fortnite is still recording" : "the header cannot be read",
                };
            }

            var info = r.Replay.Info;
            var hasTimes = info.FileVersion >= Unreal.Core.Models.ReplayVersionHistory.HISTORY_STREAM_CHUNK_TIMES;
            string? guid = null, branch = null, problem = null;
            var dataChunks = 0;
            uint? dataEnd = null;
            var span = bytes.AsSpan();
            long pos = chunksAt;
            while (pos < bytes.Length)
            {
                if (bytes.Length - pos < 8)
                {
                    problem = $"the file ends inside a chunk header at byte {pos}";
                    break;
                }
                var type = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)pos..]);
                var size = BinaryPrimitives.ReadInt32LittleEndian(span[(int)(pos + 4)..]);
                var start = pos + 8;
                if (size <= 0 || start + size > bytes.Length)
                {
                    // Fortnite writes a chunk first and its size last: a chunk still being written has size 0.
                    problem = $"the chunk at byte {pos} (type {type}, size {size}) does not fit in the file";
                    break;
                }
                if (type == (uint)ReplayChunkType.ReplayData)
                {
                    dataChunks++;
                    if (hasTimes && size >= 12)
                    {
                        var end = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)(start + 4)..]);
                        dataEnd = dataEnd is { } known && known > end ? known : end;
                    }
                }
                else if (type == (uint)ReplayChunkType.Header && guid == null)
                {
                    try
                    {
                        using var header = new Unreal.Core.BinaryReader(bytes.AsMemory((int)start, size)) { ReplayVersion = info.FileVersion };
                        r.ReadReplayHeader(header);
                        guid = r.Replay.Header?.Guid;
                        branch = r.Replay.Header?.Branch;
                    }
                    catch
                    {
                        problem ??= "the match header cannot be read";
                    }
                }
                pos = start + size;
            }

            if (problem == null && dataChunks == 0)
            {
                problem = "there is no match data in the file";
            }
            if (problem == null && string.IsNullOrEmpty(guid))
            {
                problem = "the match header is missing";
            }
            if (problem == null && dataEnd is { } reached && reached < info.LengthInMs)
            {
                problem = $"match data ends at {reached} ms, the header says {info.LengthInMs} ms";
            }
            return new ReplayLayout
            {
                State = state,
                ReplayId = guid,
                Branch = branch,
                LengthMs = info.LengthInMs,
                DataChunks = dataChunks,
                DataEndMs = dataEnd,
                Problem = problem,
            };
        }

        public static ReplayPeek Peek(byte[] bytes)
        {
            var r = new HeaderPeekReader { Replay = new FortniteReplay() };
            using var archive = new Unreal.Core.BinaryReader(bytes);
            try
            {
                r.ReadReplayInfo(archive);
            }
            catch (InvalidReplayException ex) when (ex.Message.Contains("not yet marked as completed", StringComparison.OrdinalIgnoreCase))
            {
                return new ReplayPeek { Valid = true, IsLive = true };
            }

            var info = r.Replay.Info;
            string? guid = null;
            string? branch = null;
            for (var i = 0; i < 4 && !archive.AtEnd(); i++)
            {
                var type = archive.ReadUInt32();
                var size = archive.ReadInt32();
                var start = archive.Position;
                if (size <= 0 || start + size > bytes.Length)
                {
                    break;
                }
                if (type == 0)
                {
                    r.ReadReplayHeader(archive);
                    guid = r.Replay.Header?.Guid;
                    branch = r.Replay.Header?.Branch;
                    break;
                }
                archive.Seek(start + size);
            }

            var ts = info.Timestamp;
            return new ReplayPeek
            {
                Valid = true,
                IsLive = info.IsLive,
                ReplayId = guid,
                Branch = branch,
                LengthMs = info.LengthInMs,
                TimestampUtc = ts == default ? ts : DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime(),
            };
        }
    }
}
