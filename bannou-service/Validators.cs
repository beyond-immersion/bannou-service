namespace BeyondImmersion.BannouService;

public static class Validators
{
    private static IDictionary<string, Func<bool>> sValidatorLookup;
    static Validators()
    {
        sValidatorLookup = new Dictionary<string, Func<bool>>();
        foreach (var validatorInst in IServiceAttribute.GetMethodsWithAttribute(typeof(ServiceValidator)))
        {
            if (!validatorInst.Item2.IsStatic)
                continue;

            try
            {
                var validatorAttr = validatorInst.Item3 as ServiceValidator;
                if (validatorAttr == null)
                    continue;

                var validatorDel = Delegate.CreateDelegate(typeof(Func<bool>), validatorInst.Item2) as Func<bool>;
                if (validatorDel == null)
                    continue;

                sValidatorLookup[validatorAttr.Name] = validatorDel;
            }
            catch { }
        }
    }

    public static bool Run(string name, bool defaultIfMissing = false)
    {
        if (sValidatorLookup.TryGetValue(name, out var validator))
            return validator();

        return defaultIfMissing;
    }

    public static bool RunAll()
    {
        foreach (var validator in sValidatorLookup.Values)
            if (!validator())
                return false;

        return true;
    }

    [ServiceValidator("configuration")]
    public static bool ValidateConfiguration()
    {
        if (Program.Configuration == null)
        {
            Program.Logger.Log(LogLevel.Error, null, "Service configuration required, even if only with default values.");
            return false;
        }

        if (!IServiceConfiguration.IsAnyServiceEnabled())
        {
            Program.Logger.Log(LogLevel.Error, null, "Dapr services not configured to handle any roles / APIs.");
            return false;
        }

        if (!((IServiceConfiguration)Program.Configuration).Validate())
        {
            Program.Logger.Log(LogLevel.Error, null, "Missing required service configuration.");
            return false;
        }

        return true;
    }
}
