namespace RiderIlSpy;

/// <summary>
/// Pure parsed view of the property bag that <see cref="IlSpyExternalSourcesProviderHelpers.BuildCacheProperties"/>
/// emits and Rider hands back via <c>DecompilationCacheItem.Properties</c>. Used
/// to keep entry parsing out of the JetBrains-platform-bound provider class so
/// the property->fields mapping can be unit-tested directly without spinning up
/// an <see cref="JetBrains.Metadata.Reader.API.IAssembly"/> stub.
/// </summary>
/// <param name="AssemblyFilePath">The on-disk assembly the entry decompiled from.</param>
/// <param name="TypeFullName">CLR-qualified type name being decompiled.</param>
/// <param name="Moniker">Stable cache key that ties this entry to Rider's
/// navigation cache slot. Must match the value emitted by <see cref="MonikerUtil.GetTypeCacheMoniker"/>.</param>
/// <param name="FileName">Synthesized display file name (e.g. "MyType.cs").</param>
/// <param name="Mode">The <see cref="IlSpyOutputMode"/> that produced the cached content.</param>
public sealed record DecompileEntryFields(string AssemblyFilePath, string TypeFullName, string Moniker, string FileName, IlSpyOutputMode Mode);
