using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using TreeNode.Runtime;
using TreeNode.Runtime.Generated;

namespace TreeNode.Runtime.Logic
{
    /// <summary>
    /// 线程安全的反射访问器
    /// 使用缓存和延迟初始化提供高性能的反射访问
    /// </summary>
    public class ThreadSafeReflectionAccessor : INodeAccessor
    {
        private readonly Type _nodeType;
        private readonly Lazy<PropertyInfo[]> _childProperties;
        private readonly Lazy<Dictionary<string, int>> _renderOrderMap;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="nodeType">节点类型</param>
        public ThreadSafeReflectionAccessor(Type nodeType)
        {
            _nodeType = nodeType ?? throw new ArgumentNullException(nameof(nodeType));
            
            // 使用Lazy确保线程安全的延迟初始化
            _childProperties = new Lazy<PropertyInfo[]>(InitializeChildProperties, LazyThreadSafetyMode.ExecutionAndPublication);
            _renderOrderMap = new Lazy<Dictionary<string, int>>(InitializeRenderOrderMap, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        /// <summary>
        /// 收集子节点到列表 - 线程安全
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="children">子节点列表</param>
        public void CollectChildren(JsonNode node, List<JsonNode> children)
        {
            if (node == null || children == null)
                return;

            var properties = _childProperties.Value;
            
            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(node);
                    
                    if (value is JsonNode childNode)
                    {
                        children.Add(childNode);
                    }
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode jsonNodeItem)
                            {
                                children.Add(jsonNodeItem);
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的属性
                }
            }
        }

        /// <summary>
        /// 收集子节点及其元数据 - 线程安全
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="children">子节点列表(节点, 路径, 渲染顺序)</param>
        public void CollectChildrenWithMetadata(JsonNode node, List<(JsonNode, string, int)> children)
        {
            if (node == null || children == null)
                return;

            var properties = _childProperties.Value;
            var renderOrders = _renderOrderMap.Value;
            
            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(node);
                    var renderOrder = renderOrders.GetValueOrDefault(property.Name, 1000);
                    
                    if (value is JsonNode childNode)
                    {
                        children.Add((childNode, property.Name, renderOrder));
                    }
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        int index = 0;
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode jsonNodeItem)
                            {
                                var itemPath = $"{property.Name}[{index}]";
                                children.Add((jsonNodeItem, itemPath, renderOrder + index));
                            }
                            index++;
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的属性
                }
            }
        }

        /// <summary>
        /// 获取成员的UI渲染顺序
        /// </summary>
        /// <param name="memberName">成员名称</param>
        /// <returns>渲染顺序值，值越小优先级越高</returns>
        public int GetRenderOrder(string memberName)
        {
            return _renderOrderMap.Value.GetValueOrDefault(memberName, 1000);
        }

        /// <summary>
        /// 获取此访问器处理的节点类型
        /// </summary>
        /// <returns>节点类型</returns>
        public Type GetNodeType()
        {
            return _nodeType;
        }

        /// <summary>
        /// 高性能子节点收集（性能关键路径）- 线程安全
        /// </summary>
        /// <param name="node">父节点</param>
        /// <param name="buffer">输出缓冲区</param>
        /// <param name="count">输出计数</param>
        public void CollectChildrenToBuffer(JsonNode node, JsonNode[] buffer, out int count)
        {
            count = 0;
            if (node == null || buffer == null)
                return;

            var properties = _childProperties.Value;
            
            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(node);
                    
                    if (value is JsonNode childNode && count < buffer.Length)
                    {
                        buffer[count++] = childNode;
                    }
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode jsonNodeItem && count < buffer.Length)
                            {
                                buffer[count++] = jsonNodeItem;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的属性
                }
            }
        }

        /// <summary>
        /// 快速检查是否有子节点 - 线程安全
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns>是否有子节点</returns>
        public bool HasChildren(JsonNode node)
        {
            if (node == null)
                return false;

            var properties = _childProperties.Value;
            
            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(node);
                    
                    if (value is JsonNode)
                    {
                        return true;
                    }
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode)
                            {
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的属性
                }
            }
            
            return false;
        }

        /// <summary>
        /// 获取子节点数量 - 线程安全
        /// </summary>
        /// <param name="node">节点</param>
        /// <returns>子节点数量</returns>
        public int GetChildCount(JsonNode node)
        {
            if (node == null)
                return 0;

            int count = 0;
            var properties = _childProperties.Value;
            
            foreach (var property in properties)
            {
                try
                {
                    var value = property.GetValue(node);
                    
                    if (value is JsonNode)
                    {
                        count++;
                    }
                    else if (value is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item is JsonNode)
                            {
                                count++;
                            }
                        }
                    }
                }
                catch
                {
                    // 忽略无法访问的属性
                }
            }
            
            return count;
        }

        /// <summary>
        /// 初始化子属性缓存
        /// </summary>
        /// <returns>子属性数组</returns>
        private PropertyInfo[] InitializeChildProperties()
        {
            var properties = _nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(IsChildProperty)
                .ToArray();
                
            return properties;
        }

        /// <summary>
        /// 初始化渲染顺序映射
        /// </summary>
        /// <returns>渲染顺序字典</returns>
        private Dictionary<string, int> InitializeRenderOrderMap()
        {
            var map = new Dictionary<string, int>();
            var properties = _nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var property in properties)
            {
                // 这里可以根据特性或约定设置渲染顺序
                // 例如：Child特性的优先级更高
                if (IsChildProperty(property))
                {
                    map[property.Name] = 100; // Child属性优先级较高
                }
                else
                {
                    map[property.Name] = 1000; // 默认优先级
                }
            }
            
            return map;
        }

        /// <summary>
        /// 判断属性是否为子节点属性
        /// </summary>
        /// <param name="property">属性信息</param>
        /// <returns>是否为子节点属性</returns>
        private static bool IsChildProperty(PropertyInfo property)
        {
            var propertyType = property.PropertyType;
            
            // 直接的JsonNode属性
            if (typeof(JsonNode).IsAssignableFrom(propertyType))
            {
                return true;
            }
            
            // JsonNode集合属性
            if (propertyType.IsGenericType)
            {
                var genericArgs = propertyType.GetGenericArguments();
                if (genericArgs.Length == 1 && typeof(JsonNode).IsAssignableFrom(genericArgs[0]))
                {
                    return true;
                }
            }
            
            // 数组类型
            if (propertyType.IsArray && typeof(JsonNode).IsAssignableFrom(propertyType.GetElementType()))
            {
                return true;
            }
            
            return false;
        }
    }
}