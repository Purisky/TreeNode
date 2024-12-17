using System;
using System.Collections.Generic;
using System.Reflection;

namespace TreeNode.Runtime
{
    public class EnumAttributeGetter
    {
        public static Dictionary<U, T> Get<U, T>() where T : Attribute where U : Enum
        {
            Dictionary<U, T> dic = new();
            Type Type = typeof(U);
            foreach (U enum_ in Enum.GetValues(Type))
            {
                FieldInfo fd = Type.GetField(enum_.ToString());
                T t = (T)fd.GetCustomAttribute(typeof(T));
                if (t != null)
                {
                    dic[enum_] = t;
                }
            }
            return dic;
        }
        public static Dictionary<U, T> GetAll<U, T>() where T : Attribute where U : Enum
        {
            Dictionary<U, T> dic = new();
            Type Type = typeof(U);
            foreach (U enum_ in Enum.GetValues(Type))
            {
                FieldInfo fd = Type.GetField(enum_.ToString());
                T t = (T)fd.GetCustomAttribute(typeof(T));
                dic[enum_] = t;
            }
            return dic;
        }
        public static Dictionary<U, string> GetLabelInfo<U>() where U : Enum
        {
            Dictionary<U, string> dic = new();
            Dictionary<U, LabelInfoAttribute> adic = Get<U, LabelInfoAttribute>();
            foreach (var item in adic)
            {
                dic.Add(item.Key, item.Value.Text);
            }
            return dic;
        }
        public static Dictionary<U, string> GetLabelInfoWithNull<U>() where U : Enum
        {
            Dictionary<U, string> dic = new();
            Dictionary<U, LabelInfoAttribute> adic = GetAll<U, LabelInfoAttribute>();

            foreach (var item in adic)
            {
                if (item.Value != null)
                {
                    dic.Add(item.Key, item.Value.Text);
                }
                else
                {
                    dic.Add(item.Key, item.Key.ToString());
                }
            }
            return dic;
        }
    }
}
