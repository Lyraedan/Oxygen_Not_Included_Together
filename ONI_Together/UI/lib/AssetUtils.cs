using ONI_Together.DebugTools;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.UI.lib
{
	public class AssetUtils
	{
		public static string ModPath => _modPath;
		private static string _modPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		//
		//works both pre and postfix
		public static Sprite AddSpriteToAssets(FileInfo file, Assets instance = null, bool overrideExisting = false)
		{
			if (instance == null)
			{
				instance = Assets.instance;
			}
			string spriteId = Path.GetFileNameWithoutExtension(file.Name);
			TryLoadTexture(file.FullName, out Texture2D texture);
			var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector3.zero);
			sprite.name = spriteId;
			if (!overrideExisting && instance.SpriteAssets.Any(spritef => spritef != null && spritef.name == spriteId))
			{
				DebugConsole.Log("Sprite " + spriteId + " was already existent in the sprite assets");
				return null;
			}
			if (overrideExisting)
				instance.SpriteAssets.RemoveAll(foundsprite2 => foundsprite2 != null && foundsprite2.name == spriteId);
			instance.SpriteAssets.Add(sprite);

			HashedString key = new HashedString(sprite.name);

			if (Assets.Sprites != null)
				Assets.Sprites[key] = sprite;

			return sprite;
		}

		//use in prefix
		public static Sprite AddSpriteToAssets(Assets instance, string spriteid, bool overrideExisting = false, TextureWrapMode mode = TextureWrapMode.Repeat)
		{
			var path = Path.Combine(ModPath, "assets");
			var texture = LoadTexture(spriteid, path);
			texture.wrapMode = mode;
			var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector3.zero);
			sprite.name = spriteid;
			if (!overrideExisting && instance.SpriteAssets.Any(spritef => spritef != null && spritef.name == spriteid))
			{
				DebugConsole.Log("Sprite " + spriteid + " was already existent in the sprite assets");
				return null;
			}
			if (overrideExisting)
				instance.SpriteAssets.RemoveAll(foundsprite2 => foundsprite2 != null && foundsprite2.name == spriteid);

			instance.SpriteAssets.Add(sprite);
			return sprite;
		}
		public static void OverrideSpriteTextures(Assets instance, FileInfo file)
		{
			string spriteId = Path.GetFileNameWithoutExtension(file.Name);
			var texture = AssetUtils.LoadTexture(file.FullName);

			if (instance.TextureAssets?.Any(foundsprite => foundsprite != null && foundsprite.name == spriteId) ?? false)
			{
				DebugConsole.Log("removed existing TextureAsset: " + spriteId);
				instance.TextureAssets.RemoveAll(foundsprite2 => foundsprite2 != null && foundsprite2.name == spriteId);
			}
			instance.TextureAssets?.Add(texture);
			if (Assets.Textures?.Any(foundsprite => foundsprite != null && foundsprite.name == spriteId) ?? false)
			{
				DebugConsole.Log("removed existing Texture: " + spriteId);
				Assets.Textures?.RemoveAll(foundsprite2 => foundsprite2 != null && foundsprite2.name == spriteId);
			}
			Assets.Textures?.Add(texture);

			if (instance.TextureAtlasAssets?.Any(TextureAtlas => TextureAtlas != null && TextureAtlas.texture != null && TextureAtlas.texture.name == spriteId) ?? false)
			{
				DebugConsole.Log("replaced Texture Atlas Asset texture: " + spriteId);
				var atlasInQuestion = instance.TextureAtlasAssets.First(TextureAtlas => TextureAtlas != null && TextureAtlas.texture != null && TextureAtlas.texture.name == spriteId);
				if (atlasInQuestion != null)
				{
					atlasInQuestion.texture = texture;
				}
			}


			if (Assets.TextureAtlases?.Any(TextureAtlas => TextureAtlas != null && TextureAtlas.texture != null && TextureAtlas.texture.name == spriteId) ?? false)
			{
				var atlasInQuestion = Assets.TextureAtlases.First(TextureAtlas => TextureAtlas != null && TextureAtlas.texture != null && TextureAtlas.texture.name == spriteId);
				if (atlasInQuestion != null)
				{
					atlasInQuestion.texture = texture;
				}
			}

			var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector3.zero);
			sprite.name = spriteId;

			if (instance.SpriteAssets?.Any(foundsprite => foundsprite != null && foundsprite.name == spriteId) ?? false)
			{
				DebugConsole.Log("removed existing SpriteAsset" + spriteId);
				instance.SpriteAssets.RemoveAll(foundsprite2 => foundsprite2 != null && foundsprite2.name == spriteId);
			}
			instance.SpriteAssets?.Add(sprite);

			if (Assets.Sprites?.ContainsKey(spriteId) ?? false)
			{
				DebugConsole.Log("removed existing Sprite" + spriteId);
				Assets.Sprites.Remove(spriteId);
			}
			if (Assets.TintedSprites?.Any(foundsprite => foundsprite != null && foundsprite.name == spriteId) ?? false)
			{
				Assets.TintedSprites.First(foundsprite => foundsprite != null && foundsprite.name == spriteId).sprite = sprite;
			}
			Assets.Sprites?.Add(spriteId, sprite);

		}
		public static bool TryLoadTexture(string path, out Texture2D texture)
		{
			texture = LoadTexture(path, true);
			return texture != null;
		}
		public static Texture2D LoadTexture(string name, string directory)
		{
			if (directory == null)
			{
				directory = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets");
			}

			string path = Path.Combine(directory, name + ".png");

			return LoadTexture(path);
		}
		public static Texture2D LoadTexture(string path, bool warnIfFailed = true, int customTextureWidth = 1, int customTextureHeight = 1)
		{
			Texture2D texture = null;

			if (File.Exists(path))
			{
				byte[] data = TryReadFile(path);
				texture = new Texture2D(customTextureWidth, customTextureHeight);
				typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) }).Invoke(null, new object[] { texture, data });
			}
			else if (warnIfFailed)
			{
				DebugConsole.LogWarning($"Could not load texture at path {path}.");
			}

			return texture;
		}
		public static byte[] TryReadFile(string texFile)
		{
			try
			{
				return File.ReadAllBytes(texFile);
			}
			catch (Exception e)
			{
				DebugConsole.LogWarning("Could not read file: " + e);
				return null;
			}
		}

		public static AssetBundle LoadAssetBundle(string assetBundleName, string path = null, bool platformSpecific = false)
		{
			foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (bundle.name == assetBundleName)
				{
					return bundle;
				}
			}

			if (path.IsNullOrWhiteSpace())
			{
				path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "assets");
			}

			if (platformSpecific)
			{
				switch (Application.platform)
				{
					case RuntimePlatform.WindowsPlayer:
						path = Path.Combine(path, "windows");
						break;
					case RuntimePlatform.LinuxPlayer:
						path = Path.Combine(path, "linux");
						break;
					case RuntimePlatform.OSXPlayer:
						path = Path.Combine(path, "mac");
						break;
				}
			}

			path = Path.Combine(path, assetBundleName);

			var assetBundle = AssetBundle.LoadFromFile(path);

			if (assetBundle == null)
			{
				DebugConsole.LogWarning($"Failed to load AssetBundle from path {path}");
				return null;
			}

			return assetBundle;
		}
	}
}
