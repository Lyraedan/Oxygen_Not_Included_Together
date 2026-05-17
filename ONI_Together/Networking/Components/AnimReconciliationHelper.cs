using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Patches.KleiPatches;
using System;
using System.Reflection;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	/// <summary>
	/// Static helper for setting animation elapsed time via reflection.
	/// Used by DuplicantStatePacket for continuous animation reconciliation.
	/// Resolves SetElapsedTime method or elapsedTime field once, then caches.
	/// </summary>
	internal static class AnimReconciliationHelper
	{
		private const float DriftThreshold = 0.15f;

		internal static void Reconcile(KBatchedAnimController kbac, HashedString animHash, KAnim.PlayMode playMode, float animSpeed, float elapsedTime, string source)
		{
			try
			{
				if (kbac.currentAnim != animHash)
				{
					KAnimControllerBase_Patches.AllowAnims();
					try
					{
						kbac.Play(animHash, playMode, animSpeed, 0f);
					}
					finally
					{
						// Invariant #10: a throw in Play must not leak globally-allowed anims.
						KAnimControllerBase_Patches.ForbidAnims();
					}
					ForceAnimUpdate(kbac, source);
					TrySetElapsedTime(kbac, elapsedTime);
                    return;
				}

				float localElapsed = kbac.GetElapsedTime();
				if (Mathf.Abs(localElapsed - elapsedTime) > DriftThreshold)
                    TrySetElapsedTime(kbac, elapsedTime);
            }
            catch (Exception ex)
			{
				DebugConsole.LogWarning($"[{source}] Anim reconciliation failed: {ex}");
			}
		}

		internal static void TrySetElapsedTime(KBatchedAnimController kbac, float elapsedTime)
		{
			try
			{
                kbac.SetElapsedTime(elapsedTime);
            } catch(Exception e)
			{
				DebugConsole.LogWarning("[AnimReconciliationHelper] Something went wrong setting elapsed time");
			}
		}

		internal static void ForceAnimUpdate(KBatchedAnimController kbac, string source)
		{
			try
			{
				kbac.SetVisiblity(true);
				kbac.forceRebuild = true;
				kbac.SuspendUpdates(false);
				kbac.ConfigureUpdateListener();
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[{source}] ForceAnimUpdate failed: {ex}");
			}
		}
	}
}
