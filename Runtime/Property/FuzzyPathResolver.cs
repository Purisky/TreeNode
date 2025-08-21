using System;
using System.Collections;
using System.Linq;
using TreeNode.Utility;
using static TreeNode.Runtime.TypeCacheSystem;

namespace TreeNode.Runtime
{
    /// <summary>
    /// 路径扩展结果
    /// </summary>
    public class PathExpansionResult
    {
        /// <summary>
        /// 原始路径
        /// </summary>
        public PAPath OriginalPath { get; set; }

        /// <summary>
        /// 扩展后的路径
        /// </summary>
        public PAPath ExpandedPath { get; set; }

        /// <summary>
        /// 是否成功扩展
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// 失败原因（用于调试）
        /// </summary>
        public string FailureReason { get; set; }

        /// <summary>
        /// 创建成功结果
        /// </summary>
        public static PathExpansionResult Success(PAPath originalPath, PAPath expandedPath)
        {
            return new PathExpansionResult
            {
                OriginalPath = originalPath,
                ExpandedPath = expandedPath,
                IsSuccess = true
            };
        }

        /// <summary>
        /// 创建失败结果
        /// </summary>
        public static PathExpansionResult Failure(PAPath originalPath, string reason)
        {
            return new PathExpansionResult
            {
                OriginalPath = originalPath,
                ExpandedPath = originalPath,
                IsSuccess = false,
                FailureReason = reason
            };
        }
    }

    /// <summary>
    /// 模糊路径解析器
    /// </summary>
    public static class FuzzyPathResolver
    {
        /// <summary>
        /// 尝试扩展路径（优先使用PAPath）
        /// </summary>
        public static PathExpansionResult TryExpandPath(PAPath path, object pathObject, Type targetValueType = null)
        {
            if (path.IsEmpty || pathObject == null)
            {
                return PathExpansionResult.Failure(path.ToString(), "路径或对象为空");
            }

            var objectType = pathObject.GetType();
            // 集合类型处理
            if (IsCollectionType(objectType))
            {
                return HandleCollectionPath(path, pathObject, targetValueType);
            }
            // 普通对象处理
            return HandleObjectPath(path, pathObject, targetValueType);
        }


        /// <summary>
        /// 处理集合路径
        /// </summary>
        private static PathExpansionResult HandleCollectionPath(PAPath path, object pathObject, Type targetValueType)
        {
            var collection = pathObject as ICollection;
            if (collection == null)
            {
                return PathExpansionResult.Failure(path.ToString(), "对象不是有效的集合类型");
            }

            if (targetValueType != null)
            {
                // Add 操作通常无需扩展，直接添加到集合
                return PathExpansionResult.Failure(path.ToString(), "Add 操作到集合通常无需路径扩展");
            }
            else
            {
                // Set/Remove 操作需要指向具体元素
                if (collection.Count == 1)
                {
                    return PathExpansionResult.Success(path.ToString(), path.ToString() + "[0]");
                }
                else if (collection.Count == 0)
                {
                    return PathExpansionResult.Failure(path.ToString(), "集合为空，无法自动扩展");
                }
                else
                {
                    return PathExpansionResult.Failure(path.ToString(), $"集合包含{collection.Count}个元素，无法确定目标元素");
                }
            }
        }

        /// <summary>
        /// 处理对象路径
        /// </summary>
        private static PathExpansionResult HandleObjectPath(PAPath path, object pathObject, Type targetValueType)
        {
            var objectType = pathObject.GetType();
            var typeInfo = GetTypeInfo(objectType);

            if (targetValueType != null)
            {
                // Add操作需要类型匹配
                return FindCompatibleMember(path, typeInfo, targetValueType);
            }
            else
            {
                // Set/Remove 操作不需要类型匹配
                return FindRemovableMember(path, typeInfo);
            }
        }

        /// <summary>
        /// 查找兼容的成员
        /// </summary>
        private static PathExpansionResult FindCompatibleMember(PAPath path, TypeReflectionInfo typeInfo, Type targetValueType)
        {
            var compatibleMembers = typeInfo.GetMembersByValueType(targetValueType);

            if (compatibleMembers.Count == 0)
            {
                return PathExpansionResult.Failure(path, $"未找到兼容 {targetValueType.Name} 类型的成员");
            }
            else if (compatibleMembers.Count == 1)
            {
                var member = compatibleMembers[0];
                return PathExpansionResult.Success(path, path + "." + member.Name);
            }
            else
            {
                var memberNames = string.Join(", ", compatibleMembers.Select(m => m.Name));
                return PathExpansionResult.Failure(path, $"找到多个兼容成员: {memberNames}");
            }
        }

        /// <summary>
        /// 查找可移除的成员
        /// </summary>
        private static PathExpansionResult FindRemovableMember(PAPath path, TypeReflectionInfo typeInfo)
        {
            var removableMembers = typeInfo.GetRemovableMembers();

            if (removableMembers.Count == 0)
            {
                return PathExpansionResult.Failure(path, "未找到可移除的成员");
            }
            else if (removableMembers.Count == 1)
            {
                var member = removableMembers[0];
                return PathExpansionResult.Success(path, path.Append(member.Name));
            }
            else
            {
                var memberNames = string.Join(", ", removableMembers.Select(m => m.Name));
                return PathExpansionResult.Failure(path, $"找到多个可移除成员: {memberNames}");
            }
        }

        /// <summary>
        /// 检查是否为集合类型
        /// </summary>
        private static bool IsCollectionType(Type type)
        {
            if (type == null || type == typeof(string))
            {
                return false;
            }

            return typeof(ICollection).IsAssignableFrom(type);
        }
    }
}
