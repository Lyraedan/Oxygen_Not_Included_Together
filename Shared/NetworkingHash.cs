using System;
using System.Security.Cryptography;
using System.Text;

namespace Shared
{
    public static class NetworkingHash
    {
        public static int ForType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            return ForString(type.FullName ?? type.Name);
        }

        public static int ForString(string identity)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(identity));
            return bytes[0]
                | bytes[1] << 8
                | bytes[2] << 16
                | bytes[3] << 24;
        }

        public static int ForConfigKey(string configKey)
        {
            if (configKey == null)
                return 0;

            unchecked
            {
                uint hash = 0;
                for (int i = 0; i < configKey.Length; i++)
                {
                    hash = char.ToLowerInvariant(configKey[i])
                        + (hash << 6)
                        + (hash << 16)
                        - hash;
                }

                return (int)hash;
            }
        }
    }
}
