using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.Profiling.HierarchyFrameDataView;

namespace TreeNode.Utility
{
    public static class VisualElementExtensions
    {
        public static void SetWidth(this VisualElement element, float width)
        {
            element.style.width = width switch
            {
                > 0 and <= 1 => new Length(width * 100, LengthUnit.Percent),
                > 1 => new Length(width, LengthUnit.Pixel),
                _ => StyleKeyword.Auto
            };
            if (width > 1) { element.style.flexGrow = 0; }

        }
        public static int GetPsuedoState(this VisualElement element)
        {
            return (int)element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element);
        }

        public static void AddPsuedoState(this VisualElement element, int state)
        {
            int result = element.GetPsuedoState() | state;
            var enumType = element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element).GetType();
            if (enumType != null && enumType.IsEnum)
            {
                object enumValue = Enum.ToObject(enumType, result);
                element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(element, enumValue);
            }
            else
            {
                Debug.Log("pseudoStates is not enum");
            }
        }

        public static void RemovePsuedoState(this VisualElement element, int state)
        {
            int result = element.GetPsuedoState();
            result &= ~state;
            var enumType = element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(element).GetType();
            if (enumType != null && enumType.IsEnum)
            {
                object enumValue = Enum.ToObject(enumType, result);
                element.GetType().GetProperty("pseudoStates", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(element, enumValue);
            }
            else
            {
                Debug.Log("pseudoStates is not enum");
            }
        }

        public static bool HasPseudoFlag(this VisualElement element, int flag)
        {
            int result = element.GetPsuedoState();
            return (result & flag) == flag;
        }

    }
}
