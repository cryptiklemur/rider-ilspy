using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RiderIlSpy.Search;
using Xunit;

namespace RiderIlSpy.Tests.Search;

public class IlSpySearchIndexTests
{
    [Fact]
    public void Add_And_Lookup_Literal_By_Trigram()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 0x70_000_001, 0x06_000_001, 0, "hello"));

        List<LiteralIndexEntry> hits = index.LookupLiteralCandidatesByTrigram("hel", caseSensitive: false);
        Assert.Single(hits);
        Assert.Equal("hello", hits[0].StringValue);
    }

    [Fact]
    public void Drop_Removes_All_Entries_For_Assembly()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 0, 0, 0, "alpha"));
        index.DropAssembly(asm);
        Assert.Empty(index.LookupLiteralCandidatesByTrigram("alp", caseSensitive: false));
    }

    [Fact]
    public void RegisterAssembly_Then_RegisteredAssemblies_Returns_The_Registered_Entry()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        AssemblyMetadata meta = new AssemblyMetadata(asm, "/x/a.dll", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1234L);
        index.RegisterAssembly(meta);

        IReadOnlyCollection<AssemblyMetadata> registered = index.RegisteredAssemblies();
        Assert.Single(registered);
        Assert.Equal(meta, registered.First());
    }

    [Fact]
    public void DropAssembly_Removes_Registered_Metadata()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.RegisterAssembly(new AssemblyMetadata(asm, "/x/a.dll", DateTime.UtcNow, 1L));
        Assert.Single(index.RegisteredAssemblies());

        index.DropAssembly(asm);
        Assert.Empty(index.RegisteredAssemblies());
    }

    [Fact]
    public void DropAssembly_Removes_Resource_Entries_Too()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddResource(new ResourceIndexEntry(asm, 0x28_000_001, "embedded.txt", null, 100L, "text/plain"));

        Assert.NotEmpty(index.LookupResourceCandidatesByTrigram("emb"));

        index.DropAssembly(asm);

        Assert.Empty(index.LookupResourceCandidatesByTrigram("emb"));
    }

    // AllLiteralEntries dedupes by (AssemblyId, UserStringToken) so a single
    // literal that spans N trigram buckets surfaces once. Without dedup, a
    // 9-char literal would surface 7 times (one per overlapping trigram).
    [Fact]
    public void AllLiteralEntries_Deduplicates_Across_Trigram_Buckets()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 0x70_000_001, 0x06_000_001, 0, "abcdef"));

        List<LiteralIndexEntry> all = index.AllLiteralEntries().ToList();
        Assert.Single(all);
        Assert.Equal("abcdef", all[0].StringValue);
    }

    // AllResourceEntries dedupes by (AssemblyId, ManifestResourceToken,
    // ParentEntryName) — same trigram-bucket spread as literals but with
    // an extra discriminator for nested .resources entries.
    [Fact]
    public void AllResourceEntries_Deduplicates_Across_Trigram_Buckets()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddResource(new ResourceIndexEntry(asm, 0x28_000_001, "settings.config", null, 100L, "text/plain"));

        List<ResourceIndexEntry> all = index.AllResourceEntries().ToList();
        Assert.Single(all);
        Assert.Equal("settings.config", all[0].ResourceName);
    }

    // Lookups return a fresh List, not the underlying bucket, so callers can
    // mutate the result without corrupting the index. Pinning this guards
    // against an "optimization" that returns the internal list directly.
    [Fact]
    public void Lookup_Returns_A_Detached_Snapshot()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.AddLiteral(new LiteralIndexEntry(asm, 1, 0, 0, "hello"));

        List<LiteralIndexEntry> first = index.LookupLiteralCandidatesByTrigram("hel", caseSensitive: false);
        first.Clear();

        List<LiteralIndexEntry> second = index.LookupLiteralCandidatesByTrigram("hel", caseSensitive: false);
        Assert.Single(second);
    }

    // RegisteredAssemblies returns a fresh List too — guards the same
    // snapshot-vs-live-view contract for the assembly dictionary.
    [Fact]
    public void RegisteredAssemblies_Returns_A_Detached_Snapshot()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        index.RegisterAssembly(new AssemblyMetadata(asm, "/x/a.dll", DateTime.UtcNow, 1L));

        IReadOnlyCollection<AssemblyMetadata> first = index.RegisteredAssemblies();
        Assert.IsType<List<AssemblyMetadata>>(first);
        ((List<AssemblyMetadata>)first).Clear();

        IReadOnlyCollection<AssemblyMetadata> second = index.RegisteredAssemblies();
        Assert.Single(second);
    }

    // Smoke test for the ReaderWriterLockSlim: parallel AddLiteral + concurrent
    // LookupLiteralCandidatesByTrigram must not deadlock or throw, and the
    // post-condition is that every literal is findable. Doesn't prove the
    // locking is correct under all interleavings, but it would surface an
    // accidental lock removal (no protection -> InvalidOperationException on
    // concurrent dictionary mutation).
    [Fact]
    public async Task Concurrent_Add_And_Lookup_Does_Not_Deadlock()
    {
        IlSpySearchIndex index = new IlSpySearchIndex();
        AssemblyId asm = AssemblyId.From("/x/a.dll");
        const int Iterations = 500;
        CancellationTokenSource cts = new CancellationTokenSource();

        Task reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                index.LookupLiteralCandidatesByTrigram("aaa", caseSensitive: false);
            }
        });

        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < Iterations; i++)
            {
                index.AddLiteral(new LiteralIndexEntry(asm, i, 0, 0, "aaa" + i));
            }
        });

        Task writerWinner = await Task.WhenAny(writer, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(writer, writerWinner);
        cts.Cancel();
        await Task.WhenAny(reader, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Equal(Iterations, index.AllLiteralEntries().Count());
    }
}
