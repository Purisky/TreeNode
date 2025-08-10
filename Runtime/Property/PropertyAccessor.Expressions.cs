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

        #region 表达式构建

        /// <summary>
        /// 构建成员访问表达式 - 使用PAPart
        /// </summary>
        private static Expression BuildMemberAccess(Expression current, PAPart part, ref Type type)
        {
            if (part.IsIndex)
            {
                return BuildIndexerAccess(current, part.Index, ref type);
            }
            else
            {
                return BuildPropertyOrFieldAccess(current, part.Name, ref type);
            }
        }

        /// <summary>
        /// 构建索引器访问表达式（增强Array支持）
        /// </summary>
        private static Expression BuildIndexerAccess(Expression current, int index, ref Type type)
        {
            // 优化：区分Array和普通索引器处理
            if (type.IsArray)
            {
                // 对于数组类型，使用Expression.ArrayAccess，它支持读写操作
                current = Expression.ArrayAccess(current, Expression.Constant(index));
                type = type.GetElementType();
            }
            else
            {
                // 对于其他集合类型，使用索引器
                var indexer = type.GetProperty("Item") ?? throw new ArgumentException($"类型 {type.Name} 不支持索引器访问");
                current = Expression.MakeIndex(current, indexer, new[] { Expression.Constant(index) });
                type = indexer.PropertyType;
            }

            return current;
        }

        /// <summary>
        /// 构建属性或字段访问表达式
        /// </summary>
        private static Expression BuildPropertyOrFieldAccess(Expression current, string memberName, ref Type type)
        {
            var property = type.GetProperty(memberName);
            if (property != null)
            {
                current = Expression.Property(current, property);
                type = property.PropertyType;
                return current;
            }

            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new ArgumentException($"成员 {memberName} 在类型 {type.Name} 中未找到");
            current = Expression.Field(current, field);
            type = field.FieldType;
            return current;
        }

        #endregion
        #region 值类型处理

        /// <summary>
        /// 优化的值类型setter - 使用PAPart
        /// </summary>
        private static object SetValueOnStructOptimized<T>(object structObj, PAPart part, T value)
        {
            var structType = structObj.GetType();
            var key = new CacheKey(structType, new PAPath(part), typeof(T));

            if (StructSetterCache.TryGetValue(key, out var cached))
            {
                var structSetter = (StructSetter<T>)cached;
                return structSetter(structObj, value);
            }

            var setter = CreateStructSetter<T>(structType, part);
            StructSetterCache[key] = setter;

            return setter(structObj, value);
        }

        /// <summary>
        /// 创建值类型setter委托 - 使用PAPart
        /// </summary>
        private static StructSetter<T> CreateStructSetter<T>(Type structType, PAPart part)
        {
            var structParam = Expression.Parameter(typeof(object), "structObj");
            var valueParam = Expression.Parameter(typeof(T), "value");
            var structVariable = Expression.Variable(structType, "structCopy");

            var assignStruct = Expression.Assign(structVariable, Expression.Convert(structParam, structType));

            Expression setValueExpression;

            if (part.IsIndex)
            {
                setValueExpression = CreateIndexerSetExpression<T>(structType, part.Index, structVariable, valueParam);
            }
            else
            {
                setValueExpression = CreatePropertyFieldSetExpression<T>(structType, part.Name, structVariable, valueParam);
            }

            var returnExpression = Expression.Convert(structVariable, typeof(object));

            var blockExpression = Expression.Block(
                new[] { structVariable },
                assignStruct,
                setValueExpression,
                returnExpression
            );

            return Expression.Lambda<StructSetter<T>>(blockExpression, structParam, valueParam).Compile();
        }

        /// <summary>
        /// 创建索引器设置表达式（增强Array支持）
        /// </summary>
        private static Expression CreateIndexerSetExpression<T>(Type structType, int index,
            ParameterExpression structVariable, ParameterExpression valueParam)
        {
            if (structType.IsArray)
            {
                // 对数组使用ArrayIndex优化
                var indexAccess = Expression.ArrayAccess(structVariable, Expression.Constant(index));
                return Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
            }
            else
            {
                // 对其他集合使用索引器
                var indexer = structType.GetProperty("Item") ?? throw new ArgumentException($"类型 {structType.Name} 不支持索引器");
                var indexAccess = Expression.MakeIndex(structVariable, indexer, new[] { Expression.Constant(index) });
                return Expression.Assign(indexAccess, Expression.Convert(valueParam, indexAccess.Type));
            }
        }

        /// <summary>
        /// 创建属性/字段设置表达式
        /// </summary>
        private static Expression CreatePropertyFieldSetExpression<T>(Type structType, string memberName,
            ParameterExpression structVariable, ParameterExpression valueParam)
        {
            var property = structType.GetProperty(memberName);
            if (property != null && property.CanWrite)
            {
                var propertyAccess = Expression.Property(structVariable, property);
                return Expression.Assign(propertyAccess, Expression.Convert(valueParam, property.PropertyType));
            }

            var field = structType.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
            {
                var fieldAccess = Expression.Field(structVariable, field);
                return Expression.Assign(fieldAccess, Expression.Convert(valueParam, field.FieldType));
            }

            throw new ArgumentException($"成员 {memberName} 在类型 {structType.Name} 中未找到或不可写");
        }

        #endregion
                #region 私有核心方法

        /// <summary>
        /// 处理值类型的设置操作
        /// </summary>
        private static void HandleValueTypeSet<T>(object rootObj, PAPath fullPath, PAPart lastPart, T value)
        {
            var parentPath = fullPath.GetParent();
            var parentObj = GetValue<object>(rootObj, parentPath);

            var parentCopy = SetValueOnStructOptimized(parentObj, lastPart, value);

            if (!parentPath.IsEmpty)
            {
                SetValue<object>(rootObj, parentPath, parentCopy);
            }
            else
            {
                throw new InvalidOperationException("无法修改值类型的根对象");
            }
        }

        #endregion
     }
}