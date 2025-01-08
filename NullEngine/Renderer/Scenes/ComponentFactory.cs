using System;
using System.Collections.Generic;
using System.Text.Json;
using NullEngine.Renderer.Components;
using System.Reflection;
using System.Linq;

namespace NullEngine.Renderer.Scenes
{
    public class ComponentData
    {
        public string Type { get; set; }
        public Dictionary<string, object> Properties { get; set; }
    }

    public static class ComponentFactory
    {
        private const string ComponentNamespace = "NullEngine.Renderer.Components";

        private static object ConvertProperty(object value, Type targetType)
        {
            if (value == null) return null;

            // Handle JsonElement for compatibility with JSON deserialization
            if (value is JsonElement jsonElement)
            {
                try
                {
                    if (targetType == typeof(float) && jsonElement.ValueKind == JsonValueKind.Number)
                        return jsonElement.GetSingle();

                    if (targetType == typeof(int) && jsonElement.ValueKind == JsonValueKind.Number)
                        return jsonElement.GetInt32();

                    if (targetType == typeof(string) && jsonElement.ValueKind == JsonValueKind.String)
                        return jsonElement.GetString();

                    if (targetType == typeof(bool) &&
                       (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False))
                        return jsonElement.GetBoolean();

                    if (targetType.IsEnum && jsonElement.ValueKind == JsonValueKind.String)
                    {
                        string enumValue = jsonElement.GetString();
                        return Enum.Parse(targetType, enumValue, true);
                    }

                    if (targetType.IsArray && jsonElement.ValueKind == JsonValueKind.Array)
                    {
                        var elementType = targetType.GetElementType();
                        var values = jsonElement.EnumerateArray()
                                                .Select(e => ConvertProperty(e, elementType))
                                                .ToArray();

                        Array array = Array.CreateInstance(elementType, values.Length);
                        Array.Copy(values, array, values.Length);
                        return array;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to convert JSON element to {targetType}: {ex.Message}", ex);
                }
            }

            // Handle direct type conversions
            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // Fallback to ChangeType for primitives
            try
            {
                return Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to convert value to {targetType}: {ex.Message}", ex);
            }
        }

        public static IComponent CreateComponent(string typeName, Dictionary<string, object> properties)
        {
            Log.Debug($"Creating component of type '{typeName}'.");

            // 1) Resolve the component type. If the user provided a fully qualified name,
            //    use it as-is. Otherwise, try the default namespace, then fallback to scanning all assemblies.
            Type componentType = ResolveComponentType(typeName);

            if (componentType == null || !typeof(IComponent).IsAssignableFrom(componentType))
            {
                Log.Warn($"Component type '{typeName}' not recognized or does not implement IComponent.");
                return null;
            }

            // 2) Instantiate the component
            IComponent component;
            try
            {
                component = (IComponent)Activator.CreateInstance(componentType);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to instantiate component '{typeName}': {ex.Message}");
                return null;
            }

            // 3) Reflect over the dictionary and set either fields or properties
            if (properties != null)
            {
                foreach (var kvp in properties)
                {
                    string propName = kvp.Key;
                    object propValue = kvp.Value;

                    // (a) First try a field (including private)
                    FieldInfo fieldInfo = componentType.GetField(propName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

                    if (fieldInfo != null)
                    {
                        try
                        {
                            object convertedValue = ConvertProperty(propValue, fieldInfo.FieldType);
                            fieldInfo.SetValue(component, convertedValue);
                            Log.Debug($"Set field '{propName}' to '{convertedValue}' on component '{typeName}'.");
                            continue; // Skip checking properties since we already set the field
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Failed to set field '{propName}' on component '{typeName}': {ex.Message}");
                            // We fall through to property handling if field set fails
                        }
                    }

                    // (b) If a field was not found (or field set failed), try a property (including private set)
                    PropertyInfo propInfo = componentType.GetProperty(propName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);

                    if (propInfo != null && propInfo.CanWrite)
                    {
                        try
                        {
                            object convertedValue = ConvertProperty(propValue, propInfo.PropertyType);
                            propInfo.SetValue(component, convertedValue, null);
                            Log.Debug($"Set property '{propName}' to '{convertedValue}' on component '{typeName}'.");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn($"Failed to set property '{propName}' on component '{typeName}': {ex.Message}");
                        }
                    }
                    else
                    {
                        // If neither field nor property was set
                        Log.Warn($"No matching field/property '{propName}' found or accessible on component '{typeName}'.");
                    }
                }
            }

            return component;
        }

        /// <summary>
        /// Attempts to resolve the component type by checking if the user provided
        /// a fully qualified name first, otherwise tries the default namespace,
        /// and finally scans all loaded assemblies if needed.
        /// </summary>
        private static Type ResolveComponentType(string typeName)
        {
            // If the user already provided a fully qualified name (with at least one '.'),
            // try loading that directly
            if (typeName.Contains('.'))
            {
                Type t = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
                if (t != null) return t;
            }

            // Otherwise, try using the default namespace
            string fullTypeName = $"{ComponentNamespace}.{typeName}";
            Type componentType = Type.GetType(fullTypeName, throwOnError: false, ignoreCase: true);
            if (componentType != null) return componentType;

            // Fallback: search in all currently loaded assemblies if not found yet
            // (useful if the user typed a partial or different namespace)
            componentType = AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName != null &&
                    t.FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                    t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            return componentType;
        }
    }
}
