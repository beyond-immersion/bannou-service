namespace BeyondImmersion.BannouService;

public static class Validators
{
    private static readonly IDictionary<string, Func<bool>> sValidatorLookup;

    static Validators()
    {
        sValidatorLookup = new Dictionary<string, Func<bool>>();
        foreach ((Type, System.Reflection.MethodInfo, IServiceAttribute) validatorInst in IServiceAttribute.GetMethodsWithAttribute(typeof(ServiceValidatorAttribute)))
        {
            if (!validatorInst.Item2.IsStatic)
                continue;

            try
            {
                if (validatorInst.Item3 is not ServiceValidatorAttribute validatorAttr)
                    continue;

                if (Delegate.CreateDelegate(typeof(Func<bool>), validatorInst.Item2) is not Func<bool> validatorDel)
                    continue;

                sValidatorLookup[validatorAttr.Name] = validatorDel;
            }
            catch { }
        }
    }

    public static bool Run(string name, bool defaultIfMissing = false) => sValidatorLookup.TryGetValue(name, out Func<bool>? validator) ? validator() : defaultIfMissing;

    public static bool RunAll(bool runThroughFailure = false)
    {
        var allSuccess = true;
        foreach (Func<bool> validator in sValidatorLookup.Values)
        {
            if (!validator())
            {
                if (!runThroughFailure)
                    return false;

                allSuccess = false;
            }
        }

        return allSuccess;
    }

    [ServiceValidator("configuration")]
    public static bool ValidateConfiguration()
    {
        Program.Logger.Log(LogLevel.Debug, null, "Executing validation for required service configuration.");

        if (Program.Configuration == null)
        {
            Program.Logger.Log(LogLevel.Error, null, "Service configuration missing.");
            return false;
        }

        if (!IDaprService.IsAnyEnabled())
        {
            Program.Logger.Log(LogLevel.Error, null, "No Dapr services have been enabled.");
            return false;
        }

        if (!IDaprService.AllHaveRequiredConfiguration())
        {
            Program.Logger.Log(LogLevel.Error, null, "Required configuration not set for enabled dapr services.");
            return false;
        }

        return true;
    }
}
