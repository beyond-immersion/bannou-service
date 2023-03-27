namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Use to provide validation method to run.
/// Run with Validators.Run(testName) or Validators.RunAll().
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class ServiceValidatorAttribute : BaseServiceAttribute
{
    /// <summary>
    /// Validation name, to allow running with Validators.Run(testName).
    /// </summary>
    public string Name { get; }
    public ServiceValidatorAttribute(string name)
        => Name = name;
}
