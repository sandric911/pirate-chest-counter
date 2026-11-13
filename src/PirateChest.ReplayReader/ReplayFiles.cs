using FortniteReplayReader.Models;
using PirateChest.Core.Trace;
using Unreal.Core.Exceptions;

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
/// File access for replays. Replays are only ever opened for reading with FileShare.ReadWrite|Delete,
/// so Fortnite can keep writing, rename or delete them. Nothing is ever written, moved or copied to disk.
/// </summary>
public static class ReplayFiles
{
    private static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);

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
    public static MatchTrace Parse(string path)
    {
        using var fs = OpenShared(path);
        var reader = new ChestReplayReader();
        var replay = reader.ReadReplay(fs);
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

        public static ReplayHeaderState Probe(byte[] bytes)
        {
            var r = new HeaderPeekReader { Replay = new FortniteReplay() };
            try
            {
                using var archive = new Unreal.Core.BinaryReader(bytes);
                r.ReadReplayInfo(archive);
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
