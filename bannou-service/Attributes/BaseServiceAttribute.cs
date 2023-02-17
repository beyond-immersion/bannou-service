using System.Reflection;
using BeyondImmersion.BannouService.Application;

namespace BeyondImmersion.BannouService.Attributes
{
    /// <summary>
    /// Base type for all custom attributes.
    /// </summary>
    public abstract class BaseServiceAttribute : Attribute
    {
        /// <summary>
        /// Will retrieve all types across all loaded assemblies with the given attribute.
        /// </summary>
        internal static List<(Type, T)> GetClassesWithAttribute<T>()
            where T : BaseServiceAttribute
        {
            List<(Type, T)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    var attr = classType.GetCustomAttribute<T>();
                    if (attr == null)
                        continue;

                    results.Add((classType, attr));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve all types across all loaded assemblies with the given attribute.
        /// </summary>
        internal static List<(Type, BaseServiceAttribute)> GetClassesWithAttribute(Type attributeType)
        {
            List<(Type, BaseServiceAttribute)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    var attr = classType.GetCustomAttribute(attributeType);
                    if (attr == null)
                        continue;

                    results.Add((classType, (BaseServiceAttribute)attr));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the PropertyInfo of all properties with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, propertyInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, PropertyInfo, T)> GetPropertiesWithAttribute<T>()
            where T : BaseServiceAttribute
        {
            List<(Type, PropertyInfo, T)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(PropertyInfo, T)> propsInfo = GetPropertiesWithAttribute<T>(classType);
                    if (propsInfo == null)
                        continue;

                    foreach (var propInfo in propsInfo)
                        results.Add((classType, propInfo.Item1, propInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the PropertyInfo of all properties with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, propertyInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, PropertyInfo, BaseServiceAttribute)> GetPropertiesWithAttribute(Type attributeType)
        {
            List<(Type, PropertyInfo, BaseServiceAttribute)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(PropertyInfo, BaseServiceAttribute)> propsInfo = GetPropertiesWithAttribute(classType, attributeType);
                    if (propsInfo == null)
                        continue;

                    foreach (var propInfo in propsInfo)
                        results.Add((classType, propInfo.Item1, propInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the FieldInfo of all fields with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, fieldInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, FieldInfo, T)> GetFieldsWithAttribute<T>()
            where T : BaseServiceAttribute
        {
            List<(Type, FieldInfo, T)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(FieldInfo, T)> fieldsInfo = GetFieldsWithAttribute<T>(classType);
                    if (fieldsInfo == null)
                        continue;

                    foreach (var fieldInfo in fieldsInfo)
                        results.Add((classType, fieldInfo.Item1, fieldInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the FieldInfo of all fields with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, fieldInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, FieldInfo, BaseServiceAttribute)> GetFieldsWithAttribute(Type attributeType)
        {
            List<(Type, FieldInfo, BaseServiceAttribute)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(FieldInfo, BaseServiceAttribute)> fieldsInfo = GetFieldsWithAttribute(classType, attributeType);
                    if (fieldsInfo == null)
                        continue;

                    foreach (var fieldInfo in fieldsInfo)
                        results.Add((classType, fieldInfo.Item1, fieldInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the MethodInfo of all methods with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, methodInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, MethodInfo, T)> GetMethodsWithAttribute<T>()
            where T : BaseServiceAttribute
        {
            List<(Type, MethodInfo, T)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(MethodInfo, T)> methodsInfo = GetMethodsWithAttribute<T>(classType);
                    if (methodsInfo == null)
                        continue;

                    foreach (var methodInfo in methodsInfo)
                        results.Add((classType, methodInfo.Item1, methodInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve the MethodInfo of all methods with the given attribute,
        /// across all types and all loaded assemblies.
        ///
        /// Return the type, methodInfo, and the attribute instance.
        /// </summary>
        internal static List<(Type, MethodInfo, BaseServiceAttribute)> GetMethodsWithAttribute(Type attributeType)
        {
            List<(Type, MethodInfo, BaseServiceAttribute)> results = new();
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (loadedAssemblies == null || loadedAssemblies.Length == 0)
                return results;

            foreach (var assembly in loadedAssemblies)
            {
                var classTypes = assembly.GetTypes();
                if (classTypes == null || classTypes.Length == 0)
                    continue;

                foreach (var classType in classTypes)
                {
                    List<(MethodInfo, BaseServiceAttribute)> methodsInfo = GetMethodsWithAttribute(classType, attributeType);
                    if (methodsInfo == null)
                        continue;

                    foreach (var methodInfo in methodsInfo)
                        results.Add((classType, methodInfo.Item1, methodInfo.Item2));
                }
            }
            return results;
        }

        /// <summary>
        /// Will retrieve PropertyInfo for all properties with the given attribute in the given class type.
        /// </summary>
        internal static List<(PropertyInfo, T)> GetPropertiesWithAttribute<T, S>(S _)
            where T : BaseServiceAttribute
            => GetPropertiesWithAttribute<T>(typeof(S));

        /// <summary>
        /// Will retrieve FieldInfo for all fields with the given attribute in the given class type.
        /// </summary>
        internal static List<(FieldInfo, T)> GetFieldsWithAttribute<T, S>(S _)
            where T : BaseServiceAttribute
            => GetFieldsWithAttribute<T>(typeof(S));

        /// <summary>
        /// Will retrieve MethodInfo for all methods with the given attribute in the given class type.
        /// </summary>
        internal static List<(MethodInfo, T)> GetMethodsWithAttribute<T, S>(S _)
            where T : BaseServiceAttribute
            => GetMethodsWithAttribute<T>(typeof(S));

        /// <summary>
        /// Will retrieve PropertyInfo for all properties with the given attribute in the given class type.
        /// </summary>
        internal static List<(PropertyInfo, BaseServiceAttribute)> GetPropertiesWithAttribute(Type classType, Type attributeType)
        {
            List<(PropertyInfo, BaseServiceAttribute)> results = new();
            PropertyInfo[] retrievedProps = classType.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedProps == null || retrievedProps.Length == 0)
                return results;

            foreach (PropertyInfo propInfo in retrievedProps)
            {
                var attrs = propInfo.GetCustomAttributes(attributeType);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((propInfo, (BaseServiceAttribute)attr));
            }
            return results;
        }

        /// <summary>
        /// Will retrieve PropertyInfo for all properties with the given attribute in the given class type.
        /// </summary>
        internal static List<(PropertyInfo, T)> GetPropertiesWithAttribute<T>(Type classType)
            where T : BaseServiceAttribute
        {
            List<(PropertyInfo, T)> results = new();
            PropertyInfo[] retrievedProps = classType.GetProperties(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedProps == null || retrievedProps.Length == 0)
                return results;

            foreach (PropertyInfo propInfo in retrievedProps)
            {
                var attrs = propInfo.GetCustomAttributes<T>();
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((propInfo, attr));
            }
            return results;
        }

        /// <summary>
        /// Will retrieve FieldInfo for all fields with the given attribute in the given class type.
        /// </summary>
        internal static List<(FieldInfo, T)> GetFieldsWithAttribute<T>(Type classType)
            where T : BaseServiceAttribute
        {
            List<(FieldInfo, T)> results = new();
            FieldInfo[] retrievedFields = classType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedFields == null || retrievedFields.Length == 0)
                return results;

            foreach (FieldInfo fieldInfo in retrievedFields)
            {
                var attrs = fieldInfo.GetCustomAttributes<T>();
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((fieldInfo, attr));
            }
            return results;
        }

        /// <summary>
        /// Will retrieve FieldInfo for all fields with the given attribute in the given class type.
        /// </summary>
        internal static List<(FieldInfo, BaseServiceAttribute)> GetFieldsWithAttribute(Type classType, Type attributeType)
        {
            List<(FieldInfo, BaseServiceAttribute)> results = new();
            FieldInfo[] retrievedFields = classType.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedFields == null || retrievedFields.Length == 0)
                return results;

            foreach (FieldInfo fieldInfo in retrievedFields)
            {
                var attrs = fieldInfo.GetCustomAttributes(attributeType);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((fieldInfo, (BaseServiceAttribute)attr));
            }
            return results;
        }

        /// <summary>
        /// Will retrieve MethodInfo for all methods with the given attribute in the given class type.
        /// </summary>
        internal static List<(MethodInfo, BaseServiceAttribute)> GetMethodsWithAttribute(Type classType, Type attributeType)
        {
            List<(MethodInfo, BaseServiceAttribute)> results = new();
            if (!typeof(BaseServiceAttribute).IsAssignableFrom(attributeType))
                return results;

            MethodInfo[] retrievedMethods = classType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedMethods == null || retrievedMethods.Length == 0)
                return results;

            foreach (MethodInfo methodInfo in retrievedMethods)
            {
                var attrs = methodInfo.GetCustomAttributes(attributeType);
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((methodInfo, (BaseServiceAttribute)attr));
            }
            return results;
        }

        /// <summary>
        /// Will retrieve MethodInfo for all methods with the given attribute in the given class type.
        /// </summary>
        internal static List<(MethodInfo, T)> GetMethodsWithAttribute<T>(Type classType)
            where T : BaseServiceAttribute
        {
            List<(MethodInfo, T)> results = new();
            MethodInfo[] retrievedMethods = classType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (retrievedMethods == null || retrievedMethods.Length == 0)
                return results;

            foreach (MethodInfo methodInfo in retrievedMethods)
            {
                var attrs = methodInfo.GetCustomAttributes<T>();
                if (attrs == null || !attrs.Any())
                    continue;

                foreach (var attr in attrs)
                    results.Add((methodInfo, attr));
            }
            return results;
        }

        internal static FieldInfo? GetFieldInfo(Type type, string fieldName)
        {
            FieldInfo fieldInfo;
            do
            {
                fieldInfo = type.GetField(fieldName,
                       BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                type = type.BaseType;
            }
            while (fieldInfo == null && type != null);
            return fieldInfo;
        }

        internal static object? GetFieldValue(object obj, string fieldName)
        {
            if (obj == null)
                return null;

            Type objType = obj.GetType();
            FieldInfo fieldInfo = GetFieldInfo(objType, fieldName);
            if (fieldInfo == null)
                return null;

            return fieldInfo.GetValue(obj);
        }
    }
}
