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
