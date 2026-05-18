// Intentionally declared in the global namespace (no `namespace` line) so that
// MetadataTypeNameBuilderTests can exercise the bare-name branch of
// BuildTypeFullName — the path that returns `name` when `def.Namespace` is
// empty. There is no other type in the test assembly that lands in the global
// namespace, so this fixture is load-bearing for that branch's coverage.
internal class GlobalNamespaceFixture
{
}
