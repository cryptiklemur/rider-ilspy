using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.Rider.Backend.Env;
using JetBrains.Rider.Backend.Product;

namespace RiderIlSpy;

[ZoneMarker]
public class ZoneMarker : IRequire<IRiderBackendFeatureZone>, IRequire<IProductWithRiderBackendEnvironmentZone>
{
}
