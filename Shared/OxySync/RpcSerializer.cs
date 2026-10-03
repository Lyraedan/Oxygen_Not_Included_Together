using System;
using System.IO;

namespace Shared.OxySync
{
    public static class RpcSerializer
    {
        public static byte[] Serialize(object[] args, Type[] argTypes)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            for (int i = 0; i < args.Length; i++)
            {
                try
                {
                    // Keep previous RPC behavior: null strings round-trip as empty strings.
                    object value = argTypes[i] == typeof(string) && args[i] == null
                        ? string.Empty
                        : args[i];

                    VariantHelper.ObjectToVariant(value).Write(writer);
                }
                catch (Exception)
                {
                    Debug.LogError($"[RpcSerializer] Serializing argType {argTypes[i]} failed");

                    // Intended for debugging purposes, crashing here will help identify the problematic argument.
                    throw;
                }
            }

            return ms.ToArray();
        }

        public static object[] Deserialize(byte[] data, Type[] argTypes)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            var result = new object[argTypes.Length];
            for (int i = 0; i < argTypes.Length; i++)
            {
                try
                {
                    Variant variant = Variant.Read(reader);
                    object value = VariantHelper.VariantToObject(variant, argTypes[i]);
                    if (argTypes[i] == typeof(string) && value == null)
                        value = string.Empty;
                    result[i] = value;
                }
                catch (Exception)
                {
                    Debug.LogError($"[RpcSerializer] Deserializing argType {argTypes[i]} failed");

                    // Intended for debugging purposes, crashing here will help identify the problematic argument.
                    throw;
                }
            }

            return result;
        }
    }
}