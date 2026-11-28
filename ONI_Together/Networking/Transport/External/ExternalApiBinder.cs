using System;
using System.Reflection;

namespace ONI_Together.Networking.Transport.External
{
    /// <summary>
    /// Reflection helpers for calling a transport object that lives in another mod's
    /// copy of the API assembly. Only public, implicit implementations are supported.
    /// </summary>
    internal static class ExternalApiBinder
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public;

        public static bool TryGetMethod(object instance, string name, Type[] parameterTypes, out MethodInfo method)
        {
            method = null;
            if (instance == null)
                return false;

            try
            {
                method = instance.GetType().GetMethod(name, InstanceFlags, null, parameterTypes, null);
                return method != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetDelegate<T>(object instance, string name, Type[] parameterTypes, out T del)
            where T : class
        {
            del = null;
            if (!TryGetMethod(instance, name, parameterTypes, out var method))
                return false;

            try
            {
                del = Delegate.CreateDelegate(typeof(T), instance, method) as T;
                return del != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryGetProperty(object instance, string name, out PropertyInfo property)
        {
            property = null;
            if (instance == null)
                return false;

            try
            {
                property = instance.GetType().GetProperty(name, InstanceFlags);
                return property != null && property.CanRead;
            }
            catch
            {
                return false;
            }
        }

        public static object GetPropertyValue(object instance, string name)
        {
            return TryGetProperty(instance, name, out var property)
                ? property.GetValue(instance, null)
                : null;
        }

        public static bool TryGetPropertyGetter<T>(object instance, string name, out Func<T> getter)
        {
            getter = null;
            if (!TryGetProperty(instance, name, out var property))
                return false;

            var method = property.GetGetMethod();
            if (method == null)
                return false;

            try
            {
                getter = (Func<T>)Delegate.CreateDelegate(typeof(Func<T>), instance, method);
                return getter != null;
            }
            catch
            {
                return false;
            }
        }

        public static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            var field = type.GetField(name, InstanceFlags);
            if (field != null)
                return field.GetValue(instance);

            var property = type.GetProperty(name, InstanceFlags);
            return property != null && property.CanRead ? property.GetValue(instance, null) : null;
        }
    }
}
