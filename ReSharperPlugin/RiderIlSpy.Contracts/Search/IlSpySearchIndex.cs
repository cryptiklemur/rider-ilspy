using System.Collections.Generic;
using System.Threading;

namespace RiderIlSpy.Search;

public sealed class IlSpySearchIndex
{
    private readonly ReaderWriterLockSlim myLock = new ReaderWriterLockSlim();
    private readonly Dictionary<string, List<LiteralIndexEntry>> myLiteralTrigrams = new Dictionary<string, List<LiteralIndexEntry>>();
    private readonly Dictionary<string, List<AttributeIndexEntry>> myAttributesByFqn = new Dictionary<string, List<AttributeIndexEntry>>();
    private readonly Dictionary<string, List<AttributeIndexEntry>> myAttributesByShort = new Dictionary<string, List<AttributeIndexEntry>>(System.StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ResourceIndexEntry>> myResourceTrigrams = new Dictionary<string, List<ResourceIndexEntry>>();
    private readonly Dictionary<AssemblyId, AssemblyMetadata> myAssemblies = new Dictionary<AssemblyId, AssemblyMetadata>();

    public void RegisterAssembly(AssemblyMetadata metadata)
    {
        myLock.EnterWriteLock();
        try { myAssemblies[metadata.Id] = metadata; }
        finally { myLock.ExitWriteLock(); }
    }

    public IReadOnlyCollection<AssemblyMetadata> RegisteredAssemblies()
    {
        myLock.EnterReadLock();
        try { return new List<AssemblyMetadata>(myAssemblies.Values); }
        finally { myLock.ExitReadLock(); }
    }

    public void AddLiteral(LiteralIndexEntry entry)
    {
        HashSet<string> trigrams = TrigramExtractor.Extract(entry.StringValue, caseSensitive: false);
        myLock.EnterWriteLock();
        try
        {
            foreach (string tg in trigrams)
            {
                if (!myLiteralTrigrams.TryGetValue(tg, out List<LiteralIndexEntry>? bucket))
                {
                    bucket = new List<LiteralIndexEntry>();
                    myLiteralTrigrams[tg] = bucket;
                }
                bucket.Add(entry);
            }
        }
        finally { myLock.ExitWriteLock(); }
    }

    public List<LiteralIndexEntry> LookupLiteralCandidatesByTrigram(string trigram, bool caseSensitive)
    {
        string key = caseSensitive ? trigram : trigram.ToLowerInvariant();
        myLock.EnterReadLock();
        try
        {
            return myLiteralTrigrams.TryGetValue(key, out List<LiteralIndexEntry>? bucket)
                ? new List<LiteralIndexEntry>(bucket)
                : [];
        }
        finally { myLock.ExitReadLock(); }
    }

    public void AddAttribute(AttributeIndexEntry entry)
    {
        myLock.EnterWriteLock();
        try
        {
            if (!myAttributesByFqn.TryGetValue(entry.AttributeTypeFullName, out List<AttributeIndexEntry>? bucket))
            {
                bucket = new List<AttributeIndexEntry>();
                myAttributesByFqn[entry.AttributeTypeFullName] = bucket;
            }
            bucket.Add(entry);

            if (!myAttributesByShort.TryGetValue(entry.AttributeTypeShortName, out List<AttributeIndexEntry>? sBucket))
            {
                sBucket = new List<AttributeIndexEntry>();
                myAttributesByShort[entry.AttributeTypeShortName] = sBucket;
            }
            sBucket.Add(entry);
        }
        finally { myLock.ExitWriteLock(); }
    }

    public List<AttributeIndexEntry> LookupAttributesByFqn(string fqn)
    {
        myLock.EnterReadLock();
        try
        {
            return myAttributesByFqn.TryGetValue(fqn, out List<AttributeIndexEntry>? bucket)
                ? new List<AttributeIndexEntry>(bucket)
                : [];
        }
        finally { myLock.ExitReadLock(); }
    }

    public List<AttributeIndexEntry> LookupAttributesByShortName(string shortName)
    {
        myLock.EnterReadLock();
        try
        {
            return myAttributesByShort.TryGetValue(shortName, out List<AttributeIndexEntry>? bucket)
                ? new List<AttributeIndexEntry>(bucket)
                : [];
        }
        finally { myLock.ExitReadLock(); }
    }

    public void AddResource(ResourceIndexEntry entry)
    {
        HashSet<string> trigrams = TrigramExtractor.Extract(entry.ResourceName, caseSensitive: false);
        myLock.EnterWriteLock();
        try
        {
            foreach (string tg in trigrams)
            {
                if (!myResourceTrigrams.TryGetValue(tg, out List<ResourceIndexEntry>? bucket))
                {
                    bucket = new List<ResourceIndexEntry>();
                    myResourceTrigrams[tg] = bucket;
                }
                bucket.Add(entry);
            }
        }
        finally { myLock.ExitWriteLock(); }
    }

    public List<ResourceIndexEntry> LookupResourceCandidatesByTrigram(string trigram)
    {
        string key = trigram.ToLowerInvariant();
        myLock.EnterReadLock();
        try
        {
            return myResourceTrigrams.TryGetValue(key, out List<ResourceIndexEntry>? bucket)
                ? new List<ResourceIndexEntry>(bucket)
                : [];
        }
        finally { myLock.ExitReadLock(); }
    }

    public System.Collections.Generic.IEnumerable<AttributeIndexEntry> AllAttributeEntries()
    {
        myLock.EnterReadLock();
        try
        {
            HashSet<(AssemblyId, string, int)> seen = new HashSet<(AssemblyId, string, int)>();
            List<AttributeIndexEntry> output = new List<AttributeIndexEntry>();
            foreach (List<AttributeIndexEntry> bucket in myAttributesByFqn.Values)
                foreach (AttributeIndexEntry e in bucket)
                    if (seen.Add((e.AssemblyId, e.AttributeTypeFullName, e.TargetMetadataToken))) output.Add(e);
            return output;
        }
        finally { myLock.ExitReadLock(); }
    }

    public System.Collections.Generic.IEnumerable<LiteralIndexEntry> AllLiteralEntries()
    {
        myLock.EnterReadLock();
        try
        {
            HashSet<(AssemblyId, int)> seen = new HashSet<(AssemblyId, int)>();
            List<LiteralIndexEntry> output = new List<LiteralIndexEntry>();
            foreach (List<LiteralIndexEntry> bucket in myLiteralTrigrams.Values)
                foreach (LiteralIndexEntry e in bucket)
                    if (seen.Add((e.AssemblyId, e.UserStringToken))) output.Add(e);
            return output;
        }
        finally { myLock.ExitReadLock(); }
    }

    public System.Collections.Generic.IEnumerable<ResourceIndexEntry> AllResourceEntries()
    {
        myLock.EnterReadLock();
        try
        {
            HashSet<(AssemblyId, int, string?)> seen = new HashSet<(AssemblyId, int, string?)>();
            List<ResourceIndexEntry> output = new List<ResourceIndexEntry>();
            foreach (List<ResourceIndexEntry> bucket in myResourceTrigrams.Values)
                foreach (ResourceIndexEntry e in bucket)
                    if (seen.Add((e.AssemblyId, e.ManifestResourceToken, e.ParentEntryName))) output.Add(e);
            return output;
        }
        finally { myLock.ExitReadLock(); }
    }

    public void DropAssembly(AssemblyId id)
    {
        myLock.EnterWriteLock();
        try
        {
            myAssemblies.Remove(id);
            foreach (List<LiteralIndexEntry> bucket in myLiteralTrigrams.Values) bucket.RemoveAll(e => e.AssemblyId == id);
            foreach (List<AttributeIndexEntry> bucket in myAttributesByFqn.Values) bucket.RemoveAll(e => e.AssemblyId == id);
            foreach (List<AttributeIndexEntry> bucket in myAttributesByShort.Values) bucket.RemoveAll(e => e.AssemblyId == id);
            foreach (List<ResourceIndexEntry> bucket in myResourceTrigrams.Values) bucket.RemoveAll(e => e.AssemblyId == id);
        }
        finally { myLock.ExitWriteLock(); }
    }
}
