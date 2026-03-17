using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Craft;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This class extends the generated CraftControllerBase.
/// </summary>
public partial class CraftController : CraftControllerBase
{
    public CraftController(ICraftService craftService, ITelemetryProvider telemetryProvider) : base(craftService, telemetryProvider)
    {
    }

}

