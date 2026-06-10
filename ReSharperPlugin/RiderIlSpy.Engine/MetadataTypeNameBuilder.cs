using System.Reflection.Metadata;

namespace RiderIlSpy;

/// <summary>
/// Builds a fully-qualified CLR type name from a System.Reflection.Metadata
/// <see cref="TypeDefinition"/> handle. Nested types are joined with '+' to
/// match the convention used by ILSpy and the JetBrains IClrTypeName surface.
/// Lives in its own file because both <see cref="IlSpyDecompiler"/> (per-type
/// decompile path) and <see cref="PdbSourceLinkReader"/> (SourceLink lookup
/// path) need to project a <c>TypeDefinition</c> back to its display name; a
/// shared helper keeps the two paths in lockstep so a nested-type or
/// global-namespace edge case fixed in one is automatically fixed in the other.
/// </summary>
internal static class MetadataTypeNameBuilder
{
    public static string BuildTypeFullName(MetadataReader metadata, TypeDefinition def)
    {
        string name = metadata.GetString(def.Name);
        TypeDefinitionHandle declaringHandle = def.GetDeclaringType();
        if (!declaringHandle.IsNil)
        {
            TypeDefinition decl = metadata.GetTypeDefinition(declaringHandle);
            return BuildTypeFullName(metadata, decl) + "+" + name;
        }
        string ns = metadata.GetString(def.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    /// <summary>
    /// Linear scan of <paramref name="metadata"/>'s type table for the row whose
    /// built full name (per <see cref="BuildTypeFullName"/>) equals
    /// <paramref name="typeFullName"/>. Returns <c>default</c> if not found.
    /// Both the IlSpy decompile path and the SourceLink PDB lookup need this
    /// exact projection — centralizing here means a nested-type or global-namespace
    /// edge case fixed in BuildTypeFullName is automatically honored by both.
    /// </summary>
    public static TypeDefinitionHandle FindTypeHandle(MetadataReader metadata, string typeFullName)
    {
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition def = metadata.GetTypeDefinition(handle);
            string built = BuildTypeFullName(metadata, def);
            if (built == typeFullName) return handle;
        }
        return default;
    }
}
