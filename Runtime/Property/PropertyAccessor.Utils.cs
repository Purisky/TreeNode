using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using TreeNode.Utility;
using UnityEngine;

namespace TreeNode.Runtime
{
    public static partial class PropertyAccessor
    {
        #region 验证工具类

        /// <summary>
        /// 统一的验证策略
        /// </summary>
        private static class ValidationStrategy
        {
            /// <summary>
            /// 验证成员是否存在
            /// </summary>
            public static bool ValidateMemberExists(object obj, PAPart part)
            {
                if (obj == null) return false;

                var metadata = GetOrCreateMemberInfo(obj.GetType(), part);

                if (metadata.IsIndexer)
                {
                    return IndexerStrategy.ValidateAccess(obj, part.Index);
                }
                else
                {
                    return metadata.IsProperty && metadata.Property != null ||
                           !metadata.IsProperty && metadata.Field != null;
                }
            }

            /// <summary>
            /// 验证成员是否可写
            /// </summary>
            public static bool CanWriteToMember(Type type, PAPart part)
            {
                var metadata = GetOrCreateMemberInfo(type, part);

                if (metadata.IsIndexer)
                {
                    // 数组和索引器通常可写
                    return metadata.IsArray || type.GetProperty("Item")?.CanWrite == true;
                }
                else if (metadata.IsProperty)
                {
                    return metadata.Property?.CanWrite == true;
                }
                else
                {
                    // 字段通常可写，除非是readonly
                    return metadata.Field != null && !metadata.Field.IsInitOnly;
                }
            }
        }

        #endregion

        #region 导航工具类

        /// <summary>
        /// 对象导航策略
        /// </summary>
        private static class NavigationStrategy
        {
            /// <summary>
            /// 导航到下一个对象
            /// </summary>
            public static object NavigateToNext(object rootObj, object currentObj, PAPath fullPath, int partIndex)
            {
                var part = fullPath.Parts[partIndex];
                var getter = GetOrCreateGetter<object>(currentObj.GetType(), part);
                object nextObj = getter(currentObj);

                // 如果对象为null，尝试自动创建
                if (nextObj == null)
                {
                    nextObj = TryCreateMissingObject(rootObj, fullPath, partIndex, currentObj.GetType());
                }

                return nextObj;
            }

            /// <summary>
            /// 尝试创建缺失的对象
            /// </summary>
            private static object TryCreateMissingObject(object rootObj, PAPath fullPath, int partIndex, Type parentType)
            {
                var part = fullPath.Parts[partIndex];
                var memberType = GetMemberType(parentType, part);

                if (memberType != null &&
                    !IsJsonNodeType(memberType) &&
                    HasValidParameterlessConstructor(memberType))
                {
                    var newObj = Activator.CreateInstance(memberType);
                    var parentPath = fullPath.GetSubPath(0, partIndex + 1);
                    SetValue(rootObj, parentPath, newObj);
                    return newObj;
                }

                return null;
            }
        }

        #endregion


        #region 类型工具方法

        /// <summary>
        /// 获取成员类型 - 使用PAPart
        /// </summary>
        private static Type GetMemberType(Type type, PAPart part)
        {
            var metadata = GetOrCreateMemberInfo(type, part);
            return metadata.IsProperty && metadata.Property != null
                ? metadata.Property.PropertyType
                : metadata.Field?.FieldType;
        }

        /// <summary>
        /// 检查类型是否有有效的无参构造函数
        /// </summary>
        private static bool HasValidParameterlessConstructor(Type type)
        {
            if (ConstructorCache.TryGetValue(type, out bool hasConstructor))
                return hasConstructor;

            hasConstructor = !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null;
            ConstructorCache[type] = hasConstructor;

            return hasConstructor;
        }

        /// <summary>
        /// 检查是否为JsonNode类型
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsJsonNodeType(Type type) => type == typeof(JsonNode);

        /// <summary>
        /// 检查是否为数值类型
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNumericType(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) ||
                   type == typeof(short) || type == typeof(ushort) ||
                   type == typeof(int) || type == typeof(uint) ||
                   type == typeof(long) || type == typeof(ulong) ||
                   type == typeof(float) || type == typeof(double) ||
                   type == typeof(decimal);
        }

        #endregion

        #region 字符串转换工具

        /// <summary>
        /// 将深度转换为字符串长度
        /// </summary>
        private static int ConvertDepthToStringLength(PAPath path, int validDepth)
        {
            if (validDepth <= 0) return 0;
            if (validDepth >= path.Depth) return path.OriginalPath.Length;

            // 构建有效部分的字符串长度
            var subPath = path.GetSubPath(0, validDepth);
            return subPath.OriginalPath.Length;
        }

        /// <summary>
        /// 将PAPart转换为字符串
        /// </summary>
        private static string ConvertPartToString(PAPart part)
        {
            return part.IsIndex ? $"[{part.Index}]" : part.Name;
        }

        #endregion
    }
}