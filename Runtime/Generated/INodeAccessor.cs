using System;
using System.Collections.Generic;
using TreeNode.Runtime;

namespace TreeNode.Runtime.Generated
{
    /// <summary>
    /// 高性能节点访问器接口
    /// 提供针对特定JsonNode类型的优化访问方法
    /// </summary>
    public interface INodeAccessor
    {
        /// <summary>
        /// 收集子节点到列表
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="children">子节点列表</param>
        void CollectChildren(JsonNode node, List<JsonNode> children);
        
        /// <summary>
        /// 收集子节点及其元数据
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="children">子节点列表(节点, 路径, 渲染顺序)</param>
        void CollectChildrenWithMetadata(JsonNode node, List<(JsonNode, string, int)> children);
        
        /// <summary>
        /// 获取成员的UI渲染顺序
        /// </summary>
        /// <param name="memberName">成员名称</param>
        /// <returns>渲染顺序值，值越小优先级越高</returns>
        int GetRenderOrder(string memberName);
        
        /// <summary>
        /// 获取此访问器处理的节点类型
        /// </summary>
        /// <returns>节点类型</returns>
        Type GetNodeType();
        
        /// <summary>
        /// 高性能子节点收集（性能关键路径）
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="buffer">输出缓冲区</param>
        /// <param name="count">输出计数</param>
        void CollectChildrenToBuffer(JsonNode node, JsonNode[] buffer, out int count);
        
        /// <summary>
        /// 快速检查是否有子节点
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns>是否有子节点</returns>
        bool HasChildren(JsonNode node);
        
        /// <summary>
        /// 获取子节点数量
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns>子节点数量</returns>
        int GetChildCount(JsonNode node);
    }
    
    /// <summary>
    /// 节点访问器提供者接口 - 支持依赖注入
    /// </summary>
    public interface INodeAccessorProvider
    {
        /// <summary>
        /// 获取指定类型的访问器
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <returns>访问器实例</returns>
        INodeAccessor GetAccessor(Type nodeType);
        
        /// <summary>
        /// 注册访问器
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="accessor">访问器实例</param>
        void RegisterAccessor(Type nodeType, INodeAccessor accessor);
        
        /// <summary>
        /// 尝试获取访问器
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        /// <param name="accessor">输出访问器</param>
        /// <returns>是否找到访问器</returns>
        bool TryGetAccessor(Type nodeType, out INodeAccessor accessor);
    }
}