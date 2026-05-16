using System;
using Xunit;

namespace RiderIlSpy.Tests;

/// <summary>
/// Tests for the extracted ICSharpCode.Decompiler version-compat patch. The
/// neuter has process-wide side effects, so each test runs against whatever
/// state the static has accumulated — but the API contract (idempotent
/// memoization, GetFailureReason consistency with the bool result) is
/// independently checkable regardless of run order.
/// </summary>
public class DecompilerTypeSystemPatchTests
{
    [Fact]
    public void TryNeuter_is_idempotent()
    {
        // Calling twice must return the same result — the second call returns
        // the memoized outcome without re-emitting the dynamic method. If
        // memoization were broken, the second stsfld would still succeed but
        // would be a wasteful round-trip; this test pins the contract so a
        // future refactor doesn't silently regress it.
        bool first = DecompilerTypeSystemPatch.TryNeuter();
        bool second = DecompilerTypeSystemPatch.TryNeuter();
        Assert.Equal(first, second);
    }

    [Fact]
    public void GetFailureReason_is_null_on_success_or_carries_message_on_failure()
    {
        // Two valid post-states: TryNeuter succeeded (FailureReason null) or
        // failed (FailureReason non-empty). A null reason after a false return
        // would mean the failure channel got cleared between Set and Read, which
        // is the bug this assertion guards against.
        bool succeeded = DecompilerTypeSystemPatch.TryNeuter();
        string? reason = DecompilerTypeSystemPatch.GetFailureReason();
        if (succeeded)
        {
            Assert.Null(reason);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(reason), "failed neuter must surface a reason");
        }
    }

    [Fact]
    public void IsTwoComponentTfmVersionBug_returns_false_when_paramName_is_not_fieldCount()
    {
        ArgumentException ex = new ArgumentException("not the bug", "otherParam");
        Assert.False(DecompilerTypeSystemPatch.IsTwoComponentTfmVersionBug(ex));
    }

    [Fact]
    public void IsTwoComponentTfmVersionBug_returns_false_for_unrelated_argument_exception_with_fieldCount()
    {
        // Real Version.ToString(99) throws with paramName="fieldCount" but no
        // DecompilerTypeSystem frame — exactly the false-positive case the
        // stack-frame check exists to filter out.
        ArgumentException ex;
        try
        {
            new Version(1, 0).ToString(99);
            throw new InvalidOperationException("expected ToString(99) to throw");
        }
        catch (ArgumentException caught)
        {
            ex = caught;
        }
        Assert.Equal("fieldCount", ex.ParamName);
        Assert.False(DecompilerTypeSystemPatch.IsTwoComponentTfmVersionBug(ex));
    }
}
