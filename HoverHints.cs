using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace StationItemReturn
{
    internal static class HoverHints
    {
        private const string UseKey = "$KEY_Use";
        private const string ReturnKeyCombination = "$KEY_AltPlace + $KEY_Use";

        private static readonly MethodInfo LocalizeMethod = AccessTools.Method(
            typeof(Localization),
            nameof(Localization.Localize),
            new[] { typeof(string) });

        private static readonly MethodInfo LocalizeWithReturnHintMethod = AccessTools.Method(
            typeof(HoverHints),
            nameof(LocalizeWithReturnHint),
            new[] { typeof(Localization), typeof(string), typeof(object) });

        private static string BuildReturnHint(string sharedName)
        {
            return "[<color=yellow><b>" + ReturnKeyCombination
                + "</b></color>] $hud_remove (<color=#add8e6ff>"
                + sharedName
                + "</color>)";
        }

        private static string InsertAfterFirstUseLine(string text, string hint, bool appendWhenUseIsMissing)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrEmpty(hint))
            {
                return text;
            }

            int useIndex = text.IndexOf(UseKey, StringComparison.Ordinal);
            if (useIndex < 0)
            {
                return appendWhenUseIsMissing ? text + "\n" + hint : text;
            }

            int lineEnd = text.IndexOf('\n', useIndex);
            if (lineEnd < 0)
            {
                return text + "\n" + hint;
            }

            return text.Insert(lineEnd + 1, hint + "\n");
        }

        private static string AddRawReturnHint(string text, object source)
        {
            if (!StationReturn.TryGetReturnableItem(source, out string sharedName)
                || string.IsNullOrEmpty(sharedName)
                || text.IndexOf(ReturnKeyCombination, StringComparison.Ordinal) >= 0)
            {
                return text;
            }

            return InsertAfterFirstUseLine(
                text,
                BuildReturnHint(sharedName),
                appendWhenUseIsMissing: source is Fermenter);
        }

        private static string LocalizeWithReturnHint(Localization localization, string text, object source)
        {
            return localization.Localize(AddRawReturnHint(text, source));
        }

        private static string AddLocalizedReturnHint(string text, object source)
        {
            if (string.IsNullOrWhiteSpace(text)
                || !StationReturn.TryGetReturnableItem(source, out string sharedName)
                || string.IsNullOrEmpty(sharedName))
            {
                return text;
            }

            string localizedCombination = Localization.instance.Localize(ReturnKeyCombination);
            if (!string.IsNullOrEmpty(localizedCombination)
                && text.IndexOf(localizedCombination, StringComparison.Ordinal) >= 0)
            {
                return text;
            }

            string localizedUseKey = Localization.instance.Localize(UseKey);
            string useMarkup = "[<color=yellow><b>" + localizedUseKey + "</b></color>]";
            string hint = Localization.instance.Localize(BuildReturnHint(sharedName));

            int useIndex = text.IndexOf(useMarkup, StringComparison.Ordinal);
            if (useIndex < 0)
            {
                return source is Fermenter ? text + "\n" + hint : text;
            }

            int lineEnd = text.IndexOf('\n', useIndex);
            if (lineEnd < 0)
            {
                return text + "\n" + hint;
            }

            return text.Insert(lineEnd + 1, hint + "\n");
        }

        private static IEnumerable<CodeInstruction> InjectRawReturnHint(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (LocalizeMethod != null
                    && LocalizeWithReturnHintMethod != null
                    && instruction.Calls(LocalizeMethod))
                {
                    CodeInstruction loadSource = new CodeInstruction(OpCodes.Ldarg_0);
                    loadSource.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return loadSource;

                    instruction.opcode = OpCodes.Call;
                    instruction.operand = LocalizeWithReturnHintMethod;
                }

                yield return instruction;
            }
        }

        [HarmonyPatch(typeof(CookingStation), nameof(CookingStation.GetHoverText))]
        private static class CookingStation_GetHoverText_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyAfter("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(CookingStation __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.GetHoverText))]
        private static class Fireplace_GetHoverText_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyAfter("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Fireplace __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnHoverAddOre))]
        private static class Smelter_OnHoverAddOre_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyAfter("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Smelter __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
        private static class Fermenter_GetHoverText_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyBefore("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Fermenter __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(Turret), nameof(Turret.GetHoverText))]
        private static class Turret_GetHoverText_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyAfter("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Turret __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }

        [HarmonyPatch(typeof(Switch), nameof(Switch.GetHoverText))]
        private static class Switch_GetHoverText_AddReturnHint
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                return InjectRawReturnHint(instructions);
            }

            [HarmonyAfter("shudnal.MyLittleUI")]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Switch __instance, ref string __result)
            {
                __result = AddLocalizedReturnHint(__result, __instance);
            }
        }
    }
}
