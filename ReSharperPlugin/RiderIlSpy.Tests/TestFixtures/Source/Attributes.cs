using System;

namespace Fixture.Attributes
{
    [Obsolete("Use NewType")]
    public class OldType { }

    public class HasObsoleteMember
    {
        [Obsolete] public int OldField = 0;
        [Obsolete("Use NewMethod")] public void OldMethod() { }
    }
}
