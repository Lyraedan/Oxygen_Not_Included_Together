using ONI_MP.DebugTools;
using System.IO;
using System.Reflection;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Misc
{
	public static class ResourceLoader
	{
		public static Texture2D LoadEmbeddedTexture(string resourceName)
		{
			using var _ = Profiler.Scope();

			var assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
				{
					DebugConsole.LogError($"Embedded resource not found: {resourceName}");
					return null;
				}

				byte[] buffer = new byte[stream.Length];
				stream.Read(buffer, 0, buffer.Length);

				Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
				typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }).Invoke(null, new object[] { texture, buffer });
				return texture;
			}
		}

		public static Sprite LoadEmbeddedSprite(string resourceName, out Texture2D texture)
		{
			using var _ = Profiler.Scope();
			texture = LoadEmbeddedTexture(resourceName);
			if (texture == null)
				return null;

			return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2f(0.5f, 0.5f));
		}

		// TODO: Maybe add caching to these asset bundle functions, Would it be easier to have a typed <T> loader instead of individual functions?

		public static AssetBundle LoadEmbeddedAssetBundle(string resourceName)
		{
			using var _ = Profiler.Scope();

			var assembly = Assembly.GetExecutingAssembly();
			using (Stream stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
				{
					DebugConsole.LogError($"Embedded resource not found: {resourceName}");
					return null;
				}

				byte[] buffer = new byte[stream.Length];
				stream.Read(buffer, 0, buffer.Length);

				AssetBundle bundle = AssetBundle.LoadFromMemory(buffer);
				if (bundle == null)
				{
					DebugConsole.LogError($"Failed to load AssetBundle from resource: {resourceName}");
				}
				return bundle;
			}
		}

		public static T LoadFromBundle<T>(string bundleKey, string resourceName) where T : UnityEngine.Object
        {
	        using var _ = Profiler.Scope();

			if(!MultiplayerMod.LoadedBundles.TryGetValue(bundleKey, out var bundle))
			{
                DebugConsole.LogError($"LoadFromBundle: AssetBundle with key '{bundleKey}' is not loaded!");
				return null;
            }

			T asset = bundle.LoadAsset<T>(resourceName);
			if (asset == null)
			{
                DebugConsole.LogError($"LoadFromBundle: Asset '{resourceName}' not found in bundle '{bundleKey}'!");
            } else
			{
                DebugConsole.Log($"LoadShaderFromBundle: Successfully loaded asset '{resourceName}' from bundle '{bundleKey}'.");
            }

			return asset;
        }

        public static GameObject InstantiateGameObjectFromBundle(string bundleKey, string prefabName, Transform parent = null, Vector3? position = null, Quaternion? rotation = null, Vector3? scale = null)
        {
	        using var _ = Profiler.Scope();

            GameObject prefab = LoadFromBundle<GameObject>(bundleKey, prefabName);
            if (prefab == null)
                return null;

            GameObject instance = Object.Instantiate(
                prefab,
                position ?? Vector3.zero,
                rotation ?? Quaternion.identity,
                parent
            );
			instance.transform.localScale = scale ?? Vector3.one;

            return instance;
        }


    }
}
