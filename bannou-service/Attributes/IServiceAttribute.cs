using System.Reflection;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Shared interface for all internal service attributes.
/// </summary>
public interface IServiceAttribute
{
    /// <summary>
    /// Will retrieve all types across all loaded assemblies with the given attribute.
    /// </summary>
    public static List<(Type, T)> GetClassesWithAttribute<T>()
        where T : Attribute, IServiceAttribute
    {
        List<(Type, T)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    foreach (T attr in classType.GetCustomAttributes<T>(inherit: true))
                    {
                        if (attr == null)
                            continue;

                        results.Add((classType, attr));
                    }
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve all types across all loaded assemblies with the given attribute.
    /// </summary>
    public static List<(Type, IServiceAttribute)> GetClassesWithAttribute(Type attributeType)
    {
        List<(Type, IServiceAttribute)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    foreach (var attr in classType.GetCustomAttributes(attributeType, inherit: true))
                    {
                        if (attr == null)
                            continue;

                        results.Add((classType, (IServiceAttribute)attr));
                    }
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the PropertyInfo of all properties with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, propertyInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, PropertyInfo, T)> GetPropertiesWithAttribute<T>()
        where T : Attribute, IServiceAttribute
    {
        List<(Type, PropertyInfo, T)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(PropertyInfo, T)> propsInfo = GetPropertiesWithAttribute<T>(classType);
                    if (propsInfo == null)
                        continue;

                    foreach ((PropertyInfo, T) propInfo in propsInfo)
                        results.Add((classType, propInfo.Item1, propInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the PropertyInfo of all properties with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, propertyInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, PropertyInfo, IServiceAttribute)> GetPropertiesWithAttribute(Type attributeType)
    {
        List<(Type, PropertyInfo, IServiceAttribute)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(PropertyInfo, IServiceAttribute)> propsInfo = GetPropertiesWithAttribute(classType, attributeType);
                    if (propsInfo == null)
                        continue;

                    foreach ((PropertyInfo, IServiceAttribute) propInfo in propsInfo)
                        results.Add((classType, propInfo.Item1, propInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the FieldInfo of all fields with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, fieldInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, FieldInfo, T)> GetFieldsWithAttribute<T>()
        where T : Attribute, IServiceAttribute
    {
        List<(Type, FieldInfo, T)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(FieldInfo, T)> fieldsInfo = GetFieldsWithAttribute<T>(classType);
                    if (fieldsInfo == null)
                        continue;

                    foreach ((FieldInfo, T) fieldInfo in fieldsInfo)
                        results.Add((classType, fieldInfo.Item1, fieldInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the FieldInfo of all fields with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, fieldInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, FieldInfo, IServiceAttribute)> GetFieldsWithAttribute(Type attributeType)
    {
        List<(Type, FieldInfo, IServiceAttribute)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(FieldInfo, IServiceAttribute)> fieldsInfo = GetFieldsWithAttribute(classType, attributeType);
                    if (fieldsInfo == null)
                        continue;

                    foreach ((FieldInfo, IServiceAttribute) fieldInfo in fieldsInfo)
                        results.Add((classType, fieldInfo.Item1, fieldInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the MethodInfo of all methods with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, methodInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, MethodInfo, T)> GetMethodsWithAttribute<T>()
        where T : Attribute, IServiceAttribute
    {
        List<(Type, MethodInfo, T)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(MethodInfo, T)> methodsInfo = GetMethodsWithAttribute<T>(classType);
                    if (methodsInfo == null)
                        continue;

                    foreach ((MethodInfo, T) methodInfo in methodsInfo)
                        results.Add((classType, methodInfo.Item1, methodInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve the MethodInfo of all methods with the given attribute,
    /// across all types and all loaded assemblies.
    ///
    /// Return the type, methodInfo, and the attribute instance.
    /// </summary>
    public static List<(Type, MethodInfo, IServiceAttribute)> GetMethodsWithAttribute(Type attributeType)
    {
        List<(Type, MethodInfo, IServiceAttribute)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results;

        foreach (Assembly assembly in loadedAssemblies)
        {
            try
            {
                Type[] classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (Type classType in classTypes)
                {
                    List<(MethodInfo, IServiceAttribute)> methodsInfo = GetMethodsWithAttribute(classType, attributeType);
                    if (methodsInfo == null)
                        continue;

                    foreach ((MethodInfo, IServiceAttribute) methodInfo in methodsInfo)
                        results.Add((classType, methodInfo.Item1, methodInfo.Item2));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve PropertyInfo for all properties with the given attribute in the given class type.
    /// </summary>
    public static List<(PropertyInfo, IServiceAttribute)> GetPropertiesWithAttribute(Type classType, Type attributeType)
    {
        List<(PropertyInfo, IServiceAttribute)> results = new();
        PropertyInfo[] retrievedProps = classType.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedProps == null || retrievedProps.Length == 0)
        {
            Program.Logger.Log(LogLevel.Error, "!@");
            return results;
        }

        foreach (PropertyInfo propInfo in retrievedProps)
        {
            try
            {
                var attrs = propInfo.GetCustomAttributes(attributeType, inherit: true);
                if (attrs == null || !attrs.Any())
                {
                    Program.Logger.Log(LogLevel.Error, "!!@");
                    continue;
                }

                foreach (var attr in attrs)
                {
                    Program.Logger.Log(LogLevel.Error, "!!!@");
                    results.Add((propInfo, (IServiceAttribute)attr));
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve PropertyInfo for all properties with the given attribute in the given class type.
    /// </summary>
    public static List<(PropertyInfo, T)> GetPropertiesWithAttribute<T>(Type classType)
        where T : Attribute, IServiceAttribute
    {
        List<(PropertyInfo, T)> results = new();
        PropertyInfo[] retrievedProps = classType.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedProps == null || retrievedProps.Length == 0)
            return results;

        foreach (PropertyInfo propInfo in retrievedProps)
        {
            try
            {
                IEnumerable<T> attrs = propInfo.GetCustomAttributes<T>(inherit: true);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (T attr in attrs)
                    results.Add((propInfo, attr));
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve FieldInfo for all fields with the given attribute in the given class type.
    /// </summary>
    public static List<(FieldInfo, T)> GetFieldsWithAttribute<T>(Type classType)
        where T : Attribute, IServiceAttribute
    {
        List<(FieldInfo, T)> results = new();
        FieldInfo[] retrievedFields = classType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedFields == null || retrievedFields.Length == 0)
            return results;

        foreach (FieldInfo fieldInfo in retrievedFields)
        {
            try
            {
                IEnumerable<T> attrs = fieldInfo.GetCustomAttributes<T>(inherit: true);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (T attr in attrs)
                    results.Add((fieldInfo, attr));
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve FieldInfo for all fields with the given attribute in the given class type.
    /// </summary>
    public static List<(FieldInfo, IServiceAttribute)> GetFieldsWithAttribute(Type classType, Type attributeType)
    {
        List<(FieldInfo, IServiceAttribute)> results = new();
        FieldInfo[] retrievedFields = classType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedFields == null || retrievedFields.Length == 0)
            return results;

        foreach (FieldInfo fieldInfo in retrievedFields)
        {
            try
            {
                var attrs = fieldInfo.GetCustomAttributes(attributeType, inherit: true);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((fieldInfo, (IServiceAttribute)attr));
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve MethodInfo for all methods with the given attribute in the given class type.
    /// </summary>
    public static List<(MethodInfo, IServiceAttribute)> GetMethodsWithAttribute(Type classType, Type attributeType)
    {
        List<(MethodInfo, IServiceAttribute)> results = new();
        if (!typeof(IServiceAttribute).IsAssignableFrom(attributeType))
            return results;

        MethodInfo[] retrievedMethods = classType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedMethods == null || retrievedMethods.Length == 0)
            return results;

        foreach (MethodInfo methodInfo in retrievedMethods)
        {
            try
            {
                var attrs = methodInfo.GetCustomAttributes(attributeType, inherit: true);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((methodInfo, (IServiceAttribute)attr));
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// Will retrieve MethodInfo for all methods with the given attribute in the given class type.
    /// </summary>
    public static List<(MethodInfo, T)> GetMethodsWithAttribute<T>(Type classType)
        where T : Attribute, IServiceAttribute
    {
        List<(MethodInfo, T)> results = new();
        MethodInfo[] retrievedMethods = classType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (retrievedMethods == null || retrievedMethods.Length == 0)
            return results;

        foreach (MethodInfo methodInfo in retrievedMethods)
        {
            try
            {
                IEnumerable<T> attrs = methodInfo.GetCustomAttributes<T>(inherit: true);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (T attr in attrs)
                    results.Add((methodInfo, attr));
            }
            catch { }
        }

        return results;
    }
}
