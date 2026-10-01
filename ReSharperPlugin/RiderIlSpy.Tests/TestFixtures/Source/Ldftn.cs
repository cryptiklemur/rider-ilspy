using System;

namespace Fixture.Ldftn
{
    public class LambdaHolder
    {
        public Func<string> Factory()
        {
            return () => "ldftn-needle";
        }

        public string AfterTheLambda()
        {
            return "after-ldftn-needle";
        }
    }
}
