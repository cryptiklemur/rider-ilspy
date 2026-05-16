using JetBrains.ProjectModel.Model2.Assemblies.Interfaces;

namespace RiderIlSpy;

/// <summary>
/// Immutable snapshot of one tracked decompile entry — paired with its moniker
/// in <see cref="TypeEntryCache"/>. <see cref="Mode"/> is get-only so any
/// "change the output mode of an existing entry" requires replacing the entry
/// via <see cref="TypeEntryCache.Track"/>, which holds the cache lock so the
/// swap is atomic with respect to the sync sameMode check in DecompileToCacheItem.
/// </summary>
/// <param name="Assembly">JetBrains project-model assembly handle the entry resolves through.</param>
/// <param name="AssemblyFilePath">Absolute filesystem path of the on-disk assembly file.</param>
/// <param name="TypeFullName">CLR-reflection-format type name (Namespace.Outer+Inner) for the decompiled type.</param>
/// <param name="Moniker">Stable cache key the platform's INavigationDecompilationCache uses to address this type's decompiled file.</param>
/// <param name="FileName">Synthetic .cs filename Rider shows in the editor tab.</param>
/// <param name="Mode">ILSpy output mode the entry was last decompiled with.</param>
public sealed record TypeDecompileEntry(
    IAssembly Assembly,
    string AssemblyFilePath,
    string TypeFullName,
    string Moniker,
    string FileName,
    IlSpyOutputMode Mode);
