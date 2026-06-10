using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using ICSharpCode.Decompiler.TypeSystem;

namespace RiderIlSpy;

/// <summary>
/// Self-contained version-compatibility patch for ICSharpCode.Decompiler's
/// <see cref="DecompilerTypeSystem"/>. Bundles two related concerns:
/// <list type="bullet">
///   <item>The <c>implicitReferences</c> static-field neuter — a runtime reflection
///   patch that swaps the buggy <c>System.Runtime.InteropServices</c> /
///   <c>System.Runtime.CompilerServices.Unsafe</c> implicit-reference array for
///   an empty one, side-stepping the <c>tfmVersion.ToString(3)</c> throw on
///   .NET 10+ assemblies.</item>
///   <item>The <see cref="IsTwoComponentTfmVersionBug"/> detector — recognises
///   the ArgumentException shape that signals we hit this exact bug, so callers
///   can apply the neuter and retry instead of failing the whole decompile.</item>
/// </list>
/// Lives in its own type (extracted from IlSpyDecompiler) so the orchestrator no
/// longer colocates "fix a third-party library bug via reflection" alongside the
/// straight-line decompile pipeline. The static state is private to this class —
/// the failure-reason channel is only readable through
/// <see cref="GetFailureReason"/> (snapshot under the same lock as the writers).
/// </summary>
public static class DecompilerTypeSystemPatch
{
    private static readonly object ourLock = new object();
    private static bool ourAttempted;
    private static bool ourSucceeded;
    private static string? ourFailureReason;

    /// <summary>
    /// Attempts to swap <c>DecompilerTypeSystem.implicitReferences</c> for an
    /// empty array using a <c>stsfld</c> dynamic method (bypasses initonly
    /// enforcement). Memoizes the outcome — subsequent calls return the cached
    /// success/failure without re-running the reflection probe.
    /// </summary>
    /// <returns><c>true</c> if the field was successfully neutered (now or on
    /// a prior call); <c>false</c> if any step failed — failure detail is
    /// observable via <see cref="GetFailureReason"/>.</returns>
    /// <remarks>
    /// ILSpy's <c>DecompilerTypeSystem.implicitReferences</c> is a
    /// <c>static readonly string[]</c> containing the two assemblies
    /// (<c>System.Runtime.InteropServices</c> and
    /// <c>System.Runtime.CompilerServices.Unsafe</c>) that ILSpy tries to inject
    /// as implicit references on every .NET Core/.NET 5+ decompile. The
    /// injection code calls <c>tfmVersion.ToString(3)</c> unconditionally, which
    /// throws on .NET 10+ assemblies because <c>ParseTargetFramework</c> only
    /// pads 2-component versions when the string is exactly 3 chars long
    /// (so <c>v9.0</c> → padded, <c>v10.0</c> → not padded).
    ///
    /// Swapping the static field to an empty array makes the buggy foreach a
    /// no-op for every subsequent decompile in this process. If the type
    /// actually needs those assemblies, they're already in its own AssemblyRef
    /// table and get resolved normally.
    /// </remarks>
    public static bool TryNeuter()
    {
        lock (ourLock)
        {
            if (ourAttempted) return ourSucceeded;
            ourAttempted = true;

            try
            {
                Type dts = typeof(DecompilerTypeSystem);
                FieldInfo? field = dts.GetField("implicitReferences", BindingFlags.NonPublic | BindingFlags.Static)
                                ?? dts.GetField("ImplicitReferences", BindingFlags.NonPublic | BindingFlags.Static)
                                ?? dts.GetField("_implicitReferences", BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                {
                    ourFailureReason = "could not find implicitReferences field on DecompilerTypeSystem (loaded version may be different from 8.2)";
                    ourSucceeded = false;
                    return false;
                }
                if (field.FieldType != typeof(string[]))
                {
                    ourFailureReason = "implicitReferences field has unexpected type " + field.FieldType.FullName + " (expected string[])";
                    ourSucceeded = false;
                    return false;
                }

                // .NET Core 3.0+ blocks FieldInfo.SetValue on `static readonly` (initonly)
                // fields with FieldAccessException. Emit a dynamic method that uses the
                // `stsfld` opcode directly — the verifier skips initonly checks for
                // dynamic methods when skipVisibility is true.
                DynamicMethod method = new DynamicMethod(
                    "RiderIlSpy_NeuterImplicitReferences",
                    typeof(void),
                    new[] { typeof(string[]) },
                    typeof(DecompilerTypeSystemPatch),
                    skipVisibility: true);
                ILGenerator il = method.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Stsfld, field);
                il.Emit(OpCodes.Ret);
                Action<string[]> setter = (Action<string[]>)method.CreateDelegate(typeof(Action<string[]>));
                setter(Array.Empty<string>());

                ourSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                ourFailureReason = ex.GetType().FullName + ": " + ex.Message;
                ourSucceeded = false;
                return false;
            }
        }
    }

    /// <summary>
    /// Snapshot of the most recent neuter failure reason, or <c>null</c> if the
    /// neuter has not been attempted or succeeded. Reads under the same lock as
    /// the writers so the value is consistent with the success flag.
    /// </summary>
    public static string? GetFailureReason()
    {
        lock (ourLock) return ourFailureReason;
    }

    /// <summary>
    /// Detects ILSpy's bug in <c>DecompilerTypeSystem.InitializeAsync</c>: when
    /// the assembly's TargetFramework attribute parses to a 2-component Version
    /// (e.g. <c>.NETCoreApp,Version=v9.0</c> or <c>.NETStandard,Version=v2.0</c>),
    /// ILSpy calls <c>version.ToString(3)</c> to format implicit references,
    /// which throws ArgumentException with <c>paramName="fieldCount"</c>.
    /// </summary>
    /// <remarks>
    /// Present in 8.2, 9.1, 10.0 — never been fixed upstream. CSharp and
    /// CSharpWithIL modes both go through DecompilerTypeSystem. IL-only mode
    /// uses ReflectionDisassembler and skips this entire codepath, so it always
    /// works.
    ///
    /// We match on the exception's stack frames looking for the
    /// <c>ICSharpCode.Decompiler.TypeSystem.DecompilerTypeSystem</c> origin.
    /// Type-identity beats string-matching the formatted StackTrace because it
    /// survives stack-frame omissions in Release builds and is not affected by
    /// namespace renames in user-facing trace text.
    /// </remarks>
    public static bool IsTwoComponentTfmVersionBug(ArgumentException ex)
    {
        if (ex.ParamName != "fieldCount") return false;
        StackFrame[] frames = new StackTrace(ex, fNeedFileInfo: false).GetFrames();
        foreach (StackFrame frame in frames)
        {
            Type? declaringType = frame.GetMethod()?.DeclaringType;
            if (declaringType != null && declaringType.FullName == "ICSharpCode.Decompiler.TypeSystem.DecompilerTypeSystem")
                return true;
        }
        return false;
    }
}
