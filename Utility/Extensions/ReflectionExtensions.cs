using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace TreeNode
{
    public static class ReflectionExtensions
    {

        public static bool Inherited(this Type type, Type baseType)
        {
            if (baseType.IsInterface)
            {
                Type[] interfaces = type.GetInterfaces();
                foreach (var inter in interfaces)
                {
                    if (inter == baseType)
                    {
                        return true;
                    }
                }
                return false;

            }
            return type.IsSubclassOf(baseType);

        }

        public static MemberInfo GetFirst<T>(this Type type) where T : Attribute
        {
            MemberInfo[] members = type.GetMembers();
            foreach (var member in members)
            {
                if (member.GetCustomAttribute<T>() is not null)
                {
                    return member;
                }
            }
            return null;
        }
        public static List<MemberInfo> GetAll<T>(this Type type) where T : Attribute
        {
            MemberInfo[] members = type.GetMembers();
            List<MemberInfo> list = new();
            foreach (var member in members)
            {
                if (member.GetCustomAttribute<T>() is not null)
                {
                    list.Add(member);
                }
            }
            return list;
        }
        public static List<MemberInfo> GetAll<T0, T1>(this Type type) where T0 : Attribute where T1 : Attribute
        {
            MemberInfo[] members = type.GetMembers();
            List<MemberInfo> list = new();
            foreach (var member in members)
            {
                if (member.GetCustomAttribute<T0>() is not null || member.GetCustomAttribute<T1>() is not null)
                {
                    list.Add(member);
                }
            }
            return list;
        }
        public static Type GetValueType(this MemberInfo memberInfo)
        {
            return memberInfo.MemberType switch
            {
                MemberTypes.Property => (memberInfo as PropertyInfo).PropertyType,
                MemberTypes.Field => (memberInfo as FieldInfo).FieldType,
                MemberTypes.Method => (memberInfo as MethodInfo).ReturnType,
                _ => null
            };
        }
        public static object GetValue(this MemberInfo memberInfo, object obj)
        {
            return memberInfo.MemberType switch
            {
                MemberTypes.Property => (memberInfo as PropertyInfo).GetValue(obj),
                MemberTypes.Field => (memberInfo as FieldInfo).GetValue(obj),
                _ => null
            };
        }
        public static T GetValue<T>(this MemberInfo memberInfo, object obj)
        {
            return memberInfo.MemberType switch
            {
                MemberTypes.Property => (T)(memberInfo as PropertyInfo).GetValue(obj),
                MemberTypes.Field => (T)(memberInfo as FieldInfo).GetValue(obj),
                _ => default
            };
        }
        public static void SetValue(this MemberInfo memberInfo, object obj, object value)
        {
            switch (memberInfo.MemberType)
            {
                case MemberTypes.Property:
                    (memberInfo as PropertyInfo).SetValue(obj, value);
                    break;
                case MemberTypes.Field:
                    (memberInfo as FieldInfo).SetValue(obj, value);
                    break;
            }
        }

        public static bool SerializeByJsonDotNet(this MemberInfo memberInfo)
        {
            if (memberInfo == null) { return true; }
            if (memberInfo.GetCustomAttribute<JsonIgnoreAttribute>() != null) { return false; }
            JsonObjectAttribute jsonObjectAttribute = memberInfo.ReflectedType.GetCustomAttribute<JsonObjectAttribute>();
            if (jsonObjectAttribute != null)
            {
                switch (jsonObjectAttribute.MemberSerialization)
                {
                    case MemberSerialization.OptIn:
                        if (memberInfo.GetCustomAttribute<JsonPropertyAttribute>() == null)
                        {
                            return false;
                        }
                        break;
                    case MemberSerialization.Fields:
                        if (memberInfo.MemberType != MemberTypes.Field && memberInfo.GetCustomAttribute<JsonPropertyAttribute>() == null)
                        {
                            return false;
                        }
                        break;
                }
            }
            return true;
        }
        public delegate TResult MemberGetter<out TResult>(Type type);
        public static MemberGetter<TResult> GetMemberGetter<TResult>(this MemberInfo member, object data) where TResult : class
        {
            return member.MemberType switch
            {
                MemberTypes.Field => (Type type) => (member as FieldInfo).GetValue((member as FieldInfo).IsStatic ? null : data) as TResult,
                MemberTypes.Method => HandleMethod(member as MethodInfo),
                MemberTypes.Property => (member as PropertyInfo).GetMethod.IsStatic
                    ? (member as PropertyInfo).GetMethod.CreateDelegate(typeof(MemberGetter<TResult>)) as MemberGetter<TResult>
                    : (member as PropertyInfo).GetMethod.CreateDelegate(typeof(MemberGetter<TResult>), data) as MemberGetter<TResult>,
                _ => throw new InvalidOperationException($"{member.Name} is not a valid member type for MemberGetter")
            };

            MemberGetter<TResult> HandleMethod(MethodInfo methodInfo)
            {
                if (methodInfo.GetParameters().Length > 0)
                {
                    return methodInfo.IsStatic
                    ? methodInfo.CreateDelegate(typeof(MemberGetter<TResult>)) as MemberGetter<TResult>
                    : methodInfo.CreateDelegate(typeof(MemberGetter<TResult>), data) as MemberGetter<TResult>;
                }
                else
                {
                    return (Type type) => methodInfo.Invoke(methodInfo.IsStatic ? null : data, null) as TResult;
                }
            }

        }

    }
}
