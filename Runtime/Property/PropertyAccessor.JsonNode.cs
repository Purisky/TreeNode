using System;
using System.Collections.Generic;
using System.Linq;
using TreeNode.Utility;

namespace TreeNode.Runtime
{
    /// <summary>
    /// PropertyAccessor 的 JsonNode 专用扩展
    /// 基于现有的 PropertyAccessor.CollectNodes 方法提供 JsonNode 特化接口
    /// </summary>
    public static partial class PropertyAccessor
    {
        #region JsonNode 专用操作

        /// <summary>
        /// 收集对象中的所有 JsonNode（使用现有的 PropertyAccessor.CollectNodes）
        /// </summary>
        /// <param name="root">要搜索的根对象</param>
        /// <returns>包含所有找到的 JsonNode 的 HashSet</returns>
        public static HashSet<JsonNode> CollectAllJsonNodes(object root)
        {
            if (root == null)
            {
                return new HashSet<JsonNode>();
            }

            var nodeList = new List<(PAPath path, JsonNode node)>();
            CollectNodes(root, nodeList, PAPath.Empty, depth: -1);
            
            return new HashSet<JsonNode>(nodeList.Select(item => item.node));
        }

        /// <summary>
        /// 遍历所有 JsonNode 并提供路径信息（基于现有的 PropertyAccessor.CollectNodes）
        /// </summary>
        /// <param name="root">要搜索的根对象</param>
        /// <returns>包含 JsonNode、路径和深度信息的枚举</returns>
        public static IEnumerable<(JsonNode node, PAPath path, int depth)> TraverseJsonNodeHierarchy(object root)
        {
            if (root == null)
            {
                yield break;
            }

            var nodeList = new List<(PAPath path, JsonNode node)>();
            CollectNodes(root, nodeList, PAPath.Empty, depth: -1);
            
            foreach (var item in nodeList)
            {
                yield return (item.node, item.path, item.path.Depth);
            }
        }

        /// <summary>
        /// 获取指定对象的直接 JsonNode 子节点路径（基于现有的 TypeCacheSystem）
        /// </summary>
        /// <param name="obj">要分析的对象</param>
        /// <returns>直接子节点的路径枚举</returns>
        public static IEnumerable<PAPath> GetDirectJsonNodePaths(object obj)
        {
            if (obj == null)
            {
                yield break;
            }

            var typeInfo = TypeCacheSystem.GetTypeInfo(obj.GetType());
            
            // 直接 JsonNode 成员
            foreach (var member in typeInfo.GetJsonNodeMembers())
            {
                yield return PAPath.Create(member.Name);
            }

            // 集合中的 JsonNode（只获取直接子节点）
            foreach (var member in typeInfo.GetCollectionMembers())
            {
                try
                {
                    var collection = member.Getter(obj);
                    if (collection is System.Collections.IEnumerable enumerable)
                    {
                        int index = 0;
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode)
                            {
                                yield return PAPath.Create(member.Name).AppendIndex(index);
                            }
                            index++;
                        }
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }
        }

        /// <summary>
        /// 批量获取 JsonNode 及其路径信息（使用现有的 PropertyAccessor.GetValue）
        /// </summary>
        /// <param name="root">根对象</param>
        /// <param name="paths">要获取的路径集合</param>
        /// <returns>路径到 JsonNode 的映射字典</returns>
        public static Dictionary<PAPath, JsonNode> BatchGetJsonNodes(object root, IEnumerable<PAPath> paths)
        {
            var result = new Dictionary<PAPath, JsonNode>();
            
            if (root == null || paths == null)
            {
                return result;
            }

            foreach (var path in paths)
            {
                try
                {
                    var node = GetValue<JsonNode>(root, path);
                    if (node != null)
                    {
                        result[path] = node;
                    }
                }
                catch
                {
                    // 忽略无效路径
                }
            }
            
            return result;
        }

        /// <summary>
        /// 批量更新 JsonNode（使用现有的 PropertyAccessor.SetValue）
        /// </summary>
        /// <param name="root">根对象</param>
        /// <param name="updates">路径到 JsonNode 的更新映射</param>
        public static void BatchUpdateJsonNodes(object root, Dictionary<PAPath, JsonNode> updates)
        {
            if (root == null || updates == null)
            {
                return;
            }

            foreach (var kvp in updates)
            {
                try
                {
                    SetValue(root, kvp.Key, kvp.Value);
                }
                catch
                {
                    // 跳过无法设置的路径
                }
            }
        }

        #endregion
    }
}
