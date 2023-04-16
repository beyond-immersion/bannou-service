namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Service component responsible for template definition handling.
/// </summary>
[DaprService(name: "template")]
public class TemplateService : IDaprService
{
    /// <summary>
    /// Dapr endpoint to get a specific template definition.
    /// </summary>
    public async Task Get() => await Task.CompletedTask;

    /// <summary>
    /// Dapr endpoint to list template definitions.
    /// </summary>
    public async Task List() => await Task.CompletedTask;

    /// <summary>
    /// Dapr endpoint to create new template definitions.
    /// </summary>
    public async Task Create() => await Task.CompletedTask;

    /// <summary>
    /// Dapr endpoint to update an existing template definition.
    /// </summary>
    public async Task Update() => await Task.CompletedTask;

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    public async Task Destroy() => await Task.CompletedTask;

    /// <summary>
    /// Dapr endpoint to destroy an existing template definition.
    /// </summary>
    public async Task DestroyByKey() => await Task.CompletedTask;
}
