
using System;
using System.Collections.Generic;
using System.Reflection;
using TreeNode.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
{
    public static class Extensions
    {
        public static void SetInfo(this Label label, LabelInfoAttribute info)
        {
            info ??= LabelInfoAttribute.Default;
            label.style.display = info.Hide ? DisplayStyle.None : DisplayStyle.Flex;
            label.text = info.Text;
            label.SetWidth(info.Width);
            label.style.flexGrow = 0;
            label.style.minWidth = 0;
            label.style.fontSize = info.Size;
            label.style.color = ColorUtility.TryParseHtmlString(info.Color, out Color color) ? color : LabelInfoAttribute.DefaultColor;
            label.style.alignSelf = Align.Center;
        }

        public static LabelInfoAttribute GetLabelInfo(this MemberInfo member)
        {
            LabelInfoAttribute labelInfo = member?.GetCustomAttribute<LabelInfoAttribute>() ?? new();
            labelInfo.Text ??= member?.Name;
            return labelInfo;
        }

        public static bool IsReadOnly(this MemberInfo member)
        {
            return member.GetCustomAttribute<ShowInNodeAttribute>()?.ReadOnly ?? false;
        }


        public static Action GetOnChangeAction(this MemberInfo member, object obj)
        {
            if (member?.GetCustomAttribute<OnChangeAttribute>() is OnChangeAttribute onChange)
            {
                return member.DeclaringType.GetMethod(onChange.Action).CreateDelegate(typeof(Action), obj) as Action;
            }
            return () => { };
        }
        public static MethodInfo GetMethodInfo(this MemberInfo member)
        {
            if (member?.GetCustomAttribute<OnChangeAttribute>() is OnChangeAttribute onChange)
            {
                return member.DeclaringType.GetMethod(onChange.Action);
            }
            return null;
        }
        public static Action GetOnChangeAction(this MethodInfo methodInfo, object obj)
        {
            if (methodInfo != null)
            {
                if (methodInfo.IsStatic) { 
                    return methodInfo.CreateDelegate(typeof(Action)) as Action;
                }
                return methodInfo.CreateDelegate(typeof(Action), obj) as Action;
            }
            return () => { };

        }
        public class EnumExtensions<T> where T : Enum
        {
            static Dictionary<T, string> Dic;
            static EnumExtensions()
            {
                Dic = EnumAttributeGetter.GetLabelInfo<T>();
            }
            public static string GetLabel(T enum_)
            {
                if (Dic.Count > 0 && Dic.TryGetValue(enum_, out string text))
                {
                    return text;
                }
                return enum_.ToString();
            }
        }
        public static string GetLabel<T>(this T enum_)where T : Enum
        {
           return EnumExtensions<T>.GetLabel(enum_);
        }
    }
}
