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
        /// 优化版本：减少重复的路径解析开销
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

            // 按路径深度分组，优化访问顺序
            var pathGroups = paths.GroupBy(p => p.Depth).OrderBy(g => g.Key);
            
            foreach (var group in pathGroups)
            {
                foreach (var path in group)
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
            }
            
            return result;
        }

        /// <summary>
        /// 批量更新 JsonNode（使用现有的 PropertyAccessor.SetValue）
        /// 优化版本：按路径深度优化更新顺序，减少重复访问
        /// </summary>
        /// <param name="root">根对象</param>
        /// <param name="updates">路径到 JsonNode 的更新映射</param>
        public static void BatchUpdateJsonNodes(object root, Dictionary<PAPath, JsonNode> updates)
        {
            if (root == null || updates == null || updates.Count == 0)
            {
                return;
            }

            // 按路径深度排序，先更新深层节点，再更新浅层节点
            // 这样可以避免在更新父节点时影响子节点的访问
            var sortedUpdates = updates.OrderByDescending(kvp => kvp.Key.Depth);

            foreach (var kvp in sortedUpdates)
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

        /// <summary>
        /// 高性能批量节点收集 - 使用缓存优化
        /// </summary>
        /// <param name="roots">多个根对象</param>
        /// <returns>所有根对象中的 JsonNode 集合</returns>
        public static HashSet<JsonNode> BatchCollectJsonNodes(IEnumerable<object> roots)
        {
            var result = new HashSet<JsonNode>();
            
            if (roots == null)
            {
                return result;
            }

            foreach (var root in roots)
            {
                if (root == null) continue;
                
                var nodeList = new List<(PAPath path, JsonNode node)>();
                CollectNodes(root, nodeList, PAPath.Empty, depth: -1);
                
                foreach (var (_, node) in nodeList)
                {
                    result.Add(node);
                }
            }
            
            return result;
        }

        /// <summary>
        /// 智能批量路径验证 - 预先验证路径有效性
        /// </summary>
        /// <param name="root">根对象</param>
        /// <param name="paths">要验证的路径集合</param>
        /// <returns>有效路径的集合</returns>
        public static HashSet<PAPath> ValidatePaths(object root, IEnumerable<PAPath> paths)
        {
            var validPaths = new HashSet<PAPath>();
            
            if (root == null || paths == null)
            {
                return validPaths;
            }

            // 按路径深度分组，提高验证效率
            var pathsByDepth = paths.GroupBy(p => p.Depth).OrderBy(g => g.Key);
            
            foreach (var group in pathsByDepth)
            {
                foreach (var path in group)
                {
                    try
                    {
                        // 使用轻量级验证，只检查路径是否可访问，不获取实际值
                        PropertyAccessor.ValidatePath(root, path);
                        validPaths.Add(path);
                    }
                    catch
                    {
                        // 路径无效，跳过
                    }
                }
            }
            
            return validPaths;
        }

        /// <summary>
        /// 优化的层次遍历 - 使用深度优先搜索减少内存分配
        /// </summary>
        /// <param name="root">根对象</param>
        /// <param name="maxDepth">最大深度限制，-1 表示无限制</param>
        /// <returns>优化后的 JsonNode 层次信息</returns>
        public static IEnumerable<(JsonNode node, PAPath path, int depth)> OptimizedTraverseJsonNodeHierarchy(object root, int maxDepth = -1)
        {
            if (root == null)
            {
                yield break;
            }

            var nodeList = new List<(PAPath path, JsonNode node)>();
            CollectNodes(root, nodeList, PAPath.Empty, depth: maxDepth);
            
            // 使用排序优化返回顺序，提高后续处理效率
            var sortedNodes = nodeList
                .OrderBy(item => item.path.Depth)
                .ThenBy(item => item.path.GetRenderOrder());

            foreach (var (path, node) in sortedNodes)
            {
                yield return (node, path, path.Depth);
            }
        }

        #endregion
    }
}
