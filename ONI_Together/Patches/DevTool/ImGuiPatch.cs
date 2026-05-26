using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;

namespace ONI_Together.Patches.DevTool;

[HarmonyPatch(typeof(KImGuiUtil), nameof(KImGuiUtil.SetKAssertCB))]
public static class ImGuiPatch
{
    [UsedImplicitly]
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> orig)
    {
        return new[] { new CodeInstruction(OpCodes.Ret) };
    }
}