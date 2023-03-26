namespace BeyondImmersion.BannouService.Application;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class ServiceValidator : BaseServiceAttribute
{
    public string Name { get; }
    public ServiceValidator(string name)
        => Name = name;
}
