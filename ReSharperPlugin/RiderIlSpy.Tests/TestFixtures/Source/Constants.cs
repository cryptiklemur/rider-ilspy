// Fixture for ConstantQueryHandlerTests. Each const becomes one row in the
// .NET Constant metadata table, and each row exercises one branch of
// ConstantQueryHandler.DecodeConstant's switch over ConstantTypeCode.
//
// Token values picked to be searchable without colliding: every value contains
// the substring "777" so a single Scan(..., "777") returns one hit per type
// code (plus the unsupported sentinel which decodes to "<unsupported>" — not
// matched by "777"). Boolean / Char / String use distinctive values so each
// test can assert the decoded payload precisely.
namespace Fixture.Constants
{
    public class Values
    {
        public const int Int32Value = 777;
        public const long Int64Value = 777_000_000_000L;
        public const bool BoolValue = true;
        public const string StringValue = "needle777";
        public const char CharValue = '7';
        public const double DoubleValue = 777.25;
        public const float SingleValue = 777.5f;
    }
}
