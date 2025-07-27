using System;
using System.Diagnostics;
using TreeNode.Runtime;

// 检查是否在Unity环境中，决定使用哪个Debug类
#if UNITY_EDITOR || UNITY_STANDALONE
using UnityEngine;
using Debug = UnityEngine.Debug;
#else
using Debug = System.Diagnostics.Debug;
#endif

namespace TreeNode.Editor
{
    /// <summary>
    /// JsonNodeTree性能测试和使用示例
    /// </summary>
    public static class JsonNodeTreePerformanceTest
    {
        /// <summary>
        /// 测试JsonNodeTree的性能和功能
        /// </summary>
        public static void RunPerformanceTest(TreeNodeAsset asset)
        {
            LogMessage("===== JsonNodeTree Performance Test =====");
            
            var stopwatch = new Stopwatch();
            
            // 测试树构建性能
            stopwatch.Start();
            var nodeTree = new JsonNodeTree(asset);
            stopwatch.Stop();
            LogMessage($"Tree construction time: {stopwatch.ElapsedMilliseconds}ms");
            
            // 测试基本查询性能
            stopwatch.Restart();
            var totalNodes = nodeTree.TotalNodeCount;
            var allPaths = nodeTree.GetAllNodePaths();
            var treeView = nodeTree.GetTreeView();
            stopwatch.Stop();
            LogMessage($"Basic queries time: {stopwatch.ElapsedMilliseconds}ms");
            LogMessage($"Total nodes found: {totalNodes}");
            LogMessage($"Total paths: {allPaths.Count}");
            
            // 测试缓存效率 - 重建树多次
            stopwatch.Restart();
            for (int i = 0; i < 10; i++)
            {
                nodeTree.MarkDirty();
                nodeTree.RefreshIfNeeded();
            }
            stopwatch.Stop();
            LogMessage($"10 tree rebuilds time: {stopwatch.ElapsedMilliseconds}ms");
            
            // 测试排序节点性能
            stopwatch.Restart();
            var sortedNodes = nodeTree.GetSortedNodes();
            stopwatch.Stop();
            LogMessage($"Node sorting time: {stopwatch.ElapsedMilliseconds}ms");
            LogMessage($"Sorted nodes count: {sortedNodes.Count}");
            
            // 显示树视图（截取前500字符以避免日志过长）
            var treeViewPreview = treeView.Length > 500 ? treeView.Substring(0, 500) + "..." : treeView;
            LogMessage($"Tree View Preview:\n{treeViewPreview}");
            
            // 测试验证功能
            stopwatch.Restart();
            var validationResult = nodeTree.ValidateTree();
            stopwatch.Stop();
            LogMessage($"Tree validation time: {stopwatch.ElapsedMilliseconds}ms");
            LogMessage($"Validation result: {validationResult}");
            
            // 显示缓存统计
            LogMessage("===== Cache Performance Optimization Active =====");
            LogMessage("- Type member reflection results cached");
            LogMessage("- UI render order cached and optimized");
            LogMessage("- Nested node paths (FuncValue.Node, TimeValue.Value.Node) cached");
            LogMessage("- Special type detection cached");
            
            LogMessage("===== JsonNodeTree Performance Test Complete =====");
        }
        
        /// <summary>
        /// 清理所有缓存（用于内存管理测试）
        /// </summary>
        public static void ClearAllCaches()
        {
            JsonNodeTree.ClearCache();
            LogMessage("All JsonNodeTree caches cleared");
        }
        
        /// <summary>
        /// 显示节点层次结构的详细信息
        /// </summary>
        public static void ShowDetailedNodeInfo(JsonNodeTree nodeTree, JsonNode targetNode)
        {
            var metadata = nodeTree.GetNodeMetadata(targetNode);
            if (metadata == null)
            {
                LogMessage($"No metadata found for node: {targetNode?.GetType().Name}");
                return;
            }
            
            LogMessage("===== Detailed Node Information =====");
            LogMessage($"Node Type: {targetNode.GetType().Name}");
            LogMessage($"Path: {metadata.Path}");
            LogMessage($"Depth: {metadata.Depth}");
            LogMessage($"Render Order: {metadata.RenderOrder}");
            LogMessage($"Is Root: {metadata.IsRoot}");
            LogMessage($"Is Multi Port: {metadata.IsMultiPort}");
            LogMessage($"Port Name: {metadata.PortName}");
            LogMessage($"List Index: {metadata.ListIndex}");
            LogMessage($"Root Index: {metadata.RootIndex}");
            LogMessage($"Children Count: {metadata.Children.Count}");
            
            if (metadata.Children.Count > 0)
            {
                LogMessage("Children:");
                foreach (var child in metadata.Children)
                {
                    LogMessage($"  - {child.DisplayName} (Order: {child.RenderOrder}, Port: {child.PortName})");
                }
            }
            
            // 显示祖先路径
            var ancestors = new System.Collections.Generic.List<string>();
            var current = metadata;
            while (current.Parent != null)
            {
                current = current.Parent;
                ancestors.Add(current.DisplayName);
            }
            ancestors.Reverse();
            
            if (ancestors.Count > 0)
            {
                LogMessage($"Ancestor Path: {string.Join(" -> ", ancestors)} -> {metadata.DisplayName}");
            }
            
            LogMessage("===== End Detailed Node Information =====");
        }
        
        /// <summary>
        /// 跨平台日志记录方法
        /// </summary>
        private static void LogMessage(string message)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            Debug.Log(message);
#else
            Console.WriteLine(message);
#endif
        }
    }
}