using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using ICSharpCode.Decompiler.Metadata;

namespace RiderIlSpy.Search;

/// <summary>
/// Walks metadata name tables live, the way ILSpy's own search does. Matching runs on the
/// short name; <see cref="SymbolHit.DisplayName"/> carries the qualified name for the result row.
/// </summary>
public sealed class SymbolQueryHandler
{
    public List<SymbolHit> Scan(
        IEnumerable<PEFile> assemblies,
        SymbolSearchKind kinds,
        TextMatcher matcher,
        CancellationToken ct)
    {
        List<SymbolHit> hits = new List<SymbolHit>();
        foreach (PEFile pe in assemblies)
        {
            ct.ThrowIfCancellationRequested();
            MetadataReader reader = pe.Metadata;
            AssemblyId asm = AssemblyId.From(pe.FileName);
            HashSet<string> namespacesSeen = kinds.HasFlag(SymbolSearchKind.Namespace)
                ? new HashSet<string>()
                : null!;

            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                ct.ThrowIfCancellationRequested();
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                string typeName = reader.GetString(type.Name);
                string ns = reader.GetString(type.Namespace);
                string typeDisplay = ns.Length == 0 ? typeName : ns + "." + typeName;

                if (namespacesSeen != null && ns.Length > 0 && namespacesSeen.Add(ns) && matcher.Matches(ns))
                    hits.Add(new SymbolHit(asm, SymbolSearchKind.Namespace, ns, ns, 0));

                if (kinds.HasFlag(SymbolSearchKind.Type) && matcher.Matches(typeName))
                {
                    hits.Add(new SymbolHit(
                        asm, SymbolSearchKind.Type, typeName, typeDisplay, MetadataTokens.GetToken(typeHandle)));
                }

                if (kinds.HasFlag(SymbolSearchKind.Method))
                {
                    foreach (MethodDefinitionHandle h in type.GetMethods())
                    {
                        string name = reader.GetString(reader.GetMethodDefinition(h).Name);
                        if (matcher.Matches(name))
                            hits.Add(Member(asm, SymbolSearchKind.Method, name, typeDisplay, MetadataTokens.GetToken(h)));
                    }
                }

                if (kinds.HasFlag(SymbolSearchKind.Field))
                {
                    foreach (FieldDefinitionHandle h in type.GetFields())
                    {
                        string name = reader.GetString(reader.GetFieldDefinition(h).Name);
                        if (matcher.Matches(name))
                            hits.Add(Member(asm, SymbolSearchKind.Field, name, typeDisplay, MetadataTokens.GetToken(h)));
                    }
                }

                if (kinds.HasFlag(SymbolSearchKind.Property))
                {
                    foreach (PropertyDefinitionHandle h in type.GetProperties())
                    {
                        string name = reader.GetString(reader.GetPropertyDefinition(h).Name);
                        if (matcher.Matches(name))
                            hits.Add(Member(asm, SymbolSearchKind.Property, name, typeDisplay, MetadataTokens.GetToken(h)));
                    }
                }

                if (kinds.HasFlag(SymbolSearchKind.Event))
                {
                    foreach (EventDefinitionHandle h in type.GetEvents())
                    {
                        string name = reader.GetString(reader.GetEventDefinition(h).Name);
                        if (matcher.Matches(name))
                            hits.Add(Member(asm, SymbolSearchKind.Event, name, typeDisplay, MetadataTokens.GetToken(h)));
                    }
                }
            }
        }
        return hits;
    }

    private static SymbolHit Member(
        AssemblyId asm, SymbolSearchKind kind, string name, string typeDisplay, int token) =>
        new SymbolHit(asm, kind, name, typeDisplay + "." + name, token);
}
