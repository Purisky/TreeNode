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

        #region 验证方法

        /// <summary>
        /// 验证成员是否存在 - 使用PAPart
        /// </summary>
        private static bool ValidateMemberExists(object obj, PAPart part)
        {
            if (obj == null) return false;

            var metadata = GetOrCreateMemberInfo(obj.GetType(), part);

            if (metadata.IsIndexer)
            {
                return ValidateIndexerAccess(obj, part.Index, metadata);
            }
            else
            {
                return metadata.IsProperty && metadata.Property != null ||
                       !metadata.IsProperty && metadata.Field != null;
            }
        }

        /// <summary>
        /// 验证索引器访问（增强Array验证）
        /// </summary>
        private static bool ValidateIndexerAccess(object obj, int index, MemberMetadata metadata)
        {
            object collection = obj;
            Type collectionType = obj.GetType();

            if (collection == null) return false;

            // 优先检查Array类型
            if (collectionType.IsArray && collection is Array array)
            {
                return index >= 0 && index < array.Length;
            }
            // 然后检查IList类型
            else if (collection is IList list)
            {
                return index >= 0 && index < list.Count;
            }
            // 最后检查是否有索引器
            else
            {
                return collectionType.GetProperty("Item") != null;
            }
        }

        #endregion
        #region 导航和错误处理

        /// <summary>
        /// 导航到下一个对象 - 使用PAPath和索引
        /// </summary>
        private static object NavigateToNextObject(object rootObj, object currentObj, PAPath fullPath, int partIndex)
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
        /// 尝试创建缺失的对象 - 使用PAPath
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

        #endregion
        #region 类型检查工具

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
        private static bool IsJsonNodeType(Type type) =>
            type == typeof(JsonNode);

        #endregion
        #region 工具方法

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