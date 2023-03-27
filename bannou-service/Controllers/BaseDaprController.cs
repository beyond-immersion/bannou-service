using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Easy base type for dapr controller classes.
/// </summary>
public abstract class BaseDaprController : Controller, IDaprController
{
}
