using System.Text;
using PirateChest.ReplayReader;

namespace PirateChest.Core.Tests;

/// <summary>
/// A replay is counted right after the match only if its bytes are a complete replay: final header, chunks that
/// follow one another exactly to the end of the file, match data up to the length the header states. Fortnite
/// appends chunks while it records (writing each chunk's size last) and rewrites the header when it stops, so
/// every way a file can be caught half-written must come out as "not complete".
/// </summary>
public class ReplayLayoutTests
{
    private const string Guid = "0123456789ABCDEF0123456789ABCDEF";
    private const uint Length = 600_000;

    private static byte[] Chunk(uint type, byte[] body)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);
        w.Write(body.Length);
        w.Write(body);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>The match header chunk (network demo header), as Unreal wrote it around network version 14.</summary>
    private static byte[] MatchHeader()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0x2CF5A13Du);                     // network demo magic
        w.Write(14u);                             // network version: guid and full engine version are present
        w.Write(0xAABBCCDDu);                     // network checksum
        w.Write(20u);                             // engine network version
        w.Write(0u);                              // game network protocol version
        w.Write(Convert.FromHexString(Guid));
        w.Write((ushort)43);                      // major
        w.Write((ushort)0);                       // minor
        w.Write((ushort)0);                       // patch
        w.Write(59_594_394u);                     // changelist
        var branch = "++Fortnite+Release-43.00\0";
        w.Write(branch.Length);
        w.Write(Encoding.ASCII.GetBytes(branch));
        w.Write(0u);                              // level names and times
        w.Write(0u);                              // header flags
        w.Write(0u);                              // game specific data
        w.Flush();
        return Chunk(0, ms.ToArray());
    }

    private static byte[] Data(uint startMs, uint endMs, int payload = 300)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(startMs);
        w.Write(endMs);
        w.Write((uint)payload);
        w.Write(payload);                         // size in memory (replays with encryption)
        w.Write(new byte[payload]);
        w.Flush();
        return Chunk(1, ms.ToArray());
    }

    private static byte[] Checkpoint() => Chunk(2, new byte[120]);
    private static byte[] Event() => Chunk(3, new byte[64]);

    private static byte[] Replay(bool live = false, uint lengthMs = Length, params byte[][] chunks)
    {
        var key = live ? ReplayHeaderProbeTests.ZeroKey : ReplayHeaderProbeTests.RealKey;
        return ReplayHeaderProbeTests.Header(live, encrypted: true, key, lengthMs: lengthMs, filler: false).Concat(chunks.SelectMany(c => c)).ToArray();
    }

    /// <summary>A whole match: header, data in three flushes with a checkpoint and events in between, the last flush ending at the match length.</summary>
    private static byte[][] WholeMatch() => new[]
    {
        MatchHeader(), Data(0, 200_000), Event(), Checkpoint(), Data(200_000, 400_000), Event(), Data(400_000, Length),
    };

    [Fact]
    public void Complete_WholeReplay()
    {
        var layout = ReplayFiles.Inspect(Replay(chunks: WholeMatch()));
        Assert.True(layout.Complete, layout.Problem);
        Assert.Equal(ReplayHeaderState.Finished, layout.State);
        Assert.Equal(Guid, layout.ReplayId);
        Assert.Equal("++Fortnite+Release-43.00", layout.Branch);
        Assert.Equal(Length, layout.LengthMs);
        Assert.Equal(Length, layout.DataEndMs);
        Assert.Equal(3, layout.DataChunks);
    }

    [Fact]
    public void Complete_WhenEventsFollowTheLastData()
    {
        // The match stats events are written at the very end of a match.
        var layout = ReplayFiles.Inspect(Replay(chunks: WholeMatch().Append(Event()).Append(Event()).ToArray()));
        Assert.True(layout.Complete, layout.Problem);
    }

    [Fact]
    public void NotComplete_WhileRecording()
    {
        // Everything is on disk, but the header is live and the key is not there: the data cannot even be decrypted.
        var layout = ReplayFiles.Inspect(Replay(live: true, chunks: WholeMatch()));
        Assert.False(layout.Complete);
        Assert.Equal(ReplayHeaderState.Recording, layout.State);
    }

    [Fact]
    public void NotComplete_LastDataChunkNotWrittenYet()
    {
        // A final header on a file whose last flush is missing: the parser reads such a file without complaint and
        // reports a match that is 200 s short. Only the length in the header gives it away.
        var chunks = WholeMatch();
        var layout = ReplayFiles.Inspect(Replay(chunks: chunks[..^1]));
        Assert.False(layout.Complete);
        Assert.Equal(ReplayHeaderState.Finished, layout.State);
        Assert.Contains("match data ends at 400000 ms", layout.Problem);
    }

    [Fact]
    public void NotComplete_ChunkStillBeingWritten()
    {
        // Fortnite writes a chunk and fills in its size afterwards: until then the size is 0.
        var bytes = Replay(chunks: WholeMatch());
        var lastChunkAt = bytes.Length - Data(400_000, Length).Length;
        Array.Clear(bytes, lastChunkAt + 4, 4);
        var layout = ReplayFiles.Inspect(bytes);
        Assert.False(layout.Complete);
        Assert.Contains("does not fit in the file", layout.Problem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(150)]
    [InlineData(323)]
    public void NotComplete_FileCutShort(int missing)
    {
        var bytes = Replay(chunks: WholeMatch());
        var layout = ReplayFiles.Inspect(bytes.AsSpan(0, bytes.Length - missing).ToArray());
        Assert.False(layout.Complete);
    }

    [Fact]
    public void NotComplete_AtEveryPossibleLength()
    {
        // However much of a finished replay has reached the disk, only all of it is complete.
        var bytes = Replay(chunks: WholeMatch());
        for (var length = 0; length < bytes.Length; length++)
        {
            var layout = ReplayFiles.Inspect(bytes.AsSpan(0, length).ToArray());
            Assert.False(layout.Complete, $"{length} of {bytes.Length} bytes look complete");
        }
        Assert.True(ReplayFiles.Inspect(bytes).Complete);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(64)]
    public void NotComplete_ZerosAfterTheLastChunk(int extra)
    {
        // A file whose size was set in advance (a copy in progress) is zeros where chunks are still to come.
        var layout = ReplayFiles.Inspect(Replay(chunks: WholeMatch()).Concat(new byte[extra]).ToArray());
        Assert.False(layout.Complete);
    }

    [Fact]
    public void NotComplete_WithoutMatchData()
    {
        var layout = ReplayFiles.Inspect(Replay(lengthMs: 0, chunks: new[] { MatchHeader(), Event() }));
        Assert.False(layout.Complete);
        Assert.Contains("no match data", layout.Problem);
    }

    [Fact]
    public void NotComplete_WithoutMatchHeader()
    {
        var layout = ReplayFiles.Inspect(Replay(chunks: WholeMatch()[1..]));
        Assert.False(layout.Complete);
    }

    [Fact]
    public void NotComplete_NotAReplay()
    {
        Assert.False(ReplayFiles.Inspect(Array.Empty<byte>()).Complete);
        Assert.False(ReplayFiles.Inspect(new byte[100_000]).Complete);
        Assert.Equal(ReplayHeaderState.Unreadable, ReplayFiles.Inspect(new byte[100_000]).State);
    }

    [Fact]
    public void Complete_WhenDataRunsPastTheHeaderLength()
    {
        // Only data that stops short is a sign of a missing chunk.
        var layout = ReplayFiles.Inspect(Replay(lengthMs: Length - 500, chunks: WholeMatch()));
        Assert.True(layout.Complete, layout.Problem);
    }

    [Fact]
    public void ReadAll_ReadsAFileThatAnotherProcessKeepsOpenForWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcc-layout-{System.Guid.NewGuid():N}.replay");
        try
        {
            var whole = Replay(chunks: WholeMatch());
            using (var game = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                // Recording: live header, the data so far.
                var recording = Replay(live: true, chunks: WholeMatch()[..^1]);
                game.Write(recording);
                game.Flush();
                Assert.False(ReplayFiles.Inspect(ReplayFiles.ReadAll(path)).Complete);

                // The last flush, then the final header: what Fortnite does when the match is over.
                game.Position = recording.Length;
                game.Write(whole, recording.Length, whole.Length - recording.Length);
                game.Flush();
                Assert.False(ReplayFiles.Inspect(ReplayFiles.ReadAll(path)).Complete);

                game.Position = 0;
                game.Write(whole, 0, 1024);
                game.Flush();
                Assert.True(ReplayFiles.Inspect(ReplayFiles.ReadAll(path)).Complete);
            }
            Assert.Equal(whole, ReplayFiles.ReadAll(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Of everything upstream models on a player pawn, only the fields the counting reads are decoded. The list is by
/// name: if upstream renames one of them, the reader would silently stop seeing it.
/// </summary>
public class PawnGroupsTests
{
    [Fact]
    public void UsedFields_AreFieldsUpstreamModelsOnThePlayerPawn()
    {
        var modeled = typeof(FortniteReplayReader.Models.NetFieldExports.PlayerPawn).GetProperties()
            .Select(p => p.GetCustomAttributes(typeof(Unreal.Core.Attributes.NetFieldExportAttribute), true).Cast<Unreal.Core.Attributes.NetFieldExportAttribute>().FirstOrDefault()?.Name)
            .OfType<string>()
            .ToHashSet();
        Assert.All(PawnGroups.UsedFields, f => Assert.Contains(f, modeled));
        Assert.Equal(5, PawnGroups.UsedFields.Count);
    }

    [Fact]
    public void PlayersAndBotsArePawnGroups_NothingElseIs()
    {
        Assert.True(PawnGroups.IsPawnGroup("/Game/Athena/PlayerPawn_Athena.PlayerPawn_Athena_C"));
        Assert.True(PawnGroups.IsPawnGroup("/Game/Athena/AI/Phoebe/BP_PlayerPawn_Athena_Phoebe.BP_PlayerPawn_Athena_Phoebe_C"));
        Assert.False(PawnGroups.IsPawnGroup("/Script/FortniteGame.FortPlayerStateAthena"));
        Assert.False(PawnGroups.IsPawnGroup("/Game/Building/ActorBlueprints/Containers/Tiered_Chest_Athena.Tiered_Chest_Athena_C"));
    }
}
