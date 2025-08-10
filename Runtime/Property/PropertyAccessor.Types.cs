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
        #region 内部数据结构

        /// <summary>
        /// 优化的缓存键结构 - 使用PAPath替代字符串路径
        /// </summary>
        private readonly struct CacheKey : IEquatable<CacheKey>
        {
            public readonly Type ObjectType;
            public readonly PAPath MemberPath;
            public readonly Type ValueType;
            private readonly int _hashCode;

            public CacheKey(Type objectType, PAPath memberPath, Type valueType)
            {
                ObjectType = objectType;
                MemberPath = memberPath;
                ValueType = valueType;
                _hashCode = HashCode.Combine(objectType, memberPath.GetHashCode(), valueType);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CacheKey other) =>
                ObjectType == other.ObjectType &&
                MemberPath.Equals(other.MemberPath) &&
                ValueType == other.ValueType;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public override int GetHashCode() => _hashCode;
        }

        /// <summary>
        /// 成员信息缓存结构
        /// </summary>
        private readonly struct MemberMetadata
        {
            public readonly PropertyInfo Property;
            public readonly FieldInfo Field;
            public readonly bool IsProperty;
            public readonly bool IsIndexer;
            public readonly bool IsArray;
            public readonly string CollectionName;
            public readonly bool HasCollectionName;

            public MemberMetadata(PropertyInfo property, FieldInfo field, bool isIndexer, bool isArray, string collectionName)
            {
                Property = property;
                Field = field;
                IsProperty = property != null;
                IsIndexer = isIndexer;
                IsArray = isArray;
                CollectionName = collectionName;
                HasCollectionName = !string.IsNullOrEmpty(collectionName);
            }
        }

        /// <summary>
        /// 值类型Setter委托
        /// </summary>
        private delegate object StructSetter<T>(object structObj, T value);

        #endregion
    }
}