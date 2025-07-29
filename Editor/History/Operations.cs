using System;
using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEngine;

namespace TreeNode.Editor
{
    /// <summary>
    /// 原子操作接口
    /// </summary>
    public interface IAtomicOperation
    {
        OperationType Type { get; }
        DateTime Timestamp { get; }
        string Description { get; }
        bool Execute();
        bool Undo();
        bool CanUndo();
        string GetOperationSummary();
        string GetOperationId(); // 用于防重复
    }
    /// <summary>
    /// 节点创建操作
    /// </summary>
    public class NodeCreateOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeCreate;
        public DateTime Timestamp { get; private set; }
        public string Description => $"创建节点: {Node?.GetType().Name}";

        public JsonNode Node { get; set; }
        public NodeLocation Location { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public NodeCreateOperation(JsonNode node, NodeLocation location, TreeNodeGraphView graphView)
        {
            Node = node;
            Location = location;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 执行节点创建操作 - 将节点添加到指定位置
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null || Location == null || GraphView == null)
                {
                    Debug.LogError("NodeCreateOperation.Execute: 参数不完整");
                    return false;
                }

                // 根据位置类型添加节点
                bool success = AddNodeToLocation();

                if (success)
                {
                    // 创建对应的ViewNode
                    CreateViewNode();

                    Debug.Log($"成功执行节点创建: {Node.GetType().Name} at {Location.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"执行节点创建操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销节点创建操作 - 从指定位置移除节点
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null || Location == null || GraphView == null)
                {
                    Debug.LogError("NodeCreateOperation.Undo: 参数不完整");
                    return false;
                }

                // 移除ViewNode
                RemoveViewNode();

                // 从位置移除节点
                bool success = RemoveNodeFromLocation();

                if (success)
                {
                    Debug.Log($"成功撤销节点创建: {Node.GetType().Name} from {Location.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销节点创建操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将节点添加到指定位置
        /// </summary>
        private bool AddNodeToLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (Location.Type)
                {
                    case LocationType.Root:
                        // 添加到根节点列表
                        if (Location.RootIndex >= 0 && Location.RootIndex <= asset.Data.Nodes.Count)
                        {
                            asset.Data.Nodes.Insert(Location.RootIndex, Node);
                        }
                        else
                        {
                            asset.Data.Nodes.Add(Node);
                        }
                        return true;

                    case LocationType.Child:
                        // 添加到父节点的子节点端口
                        return AddNodeToParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {Location.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"添加节点到位置失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从指定位置移除节点
        /// </summary>
        private bool RemoveNodeFromLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (Location.Type)
                {
                    case LocationType.Root:
                        // 从根节点列表移除
                        return asset.Data.Nodes.Remove(Node);

                    case LocationType.Child:
                        // 从父节点的子节点端口移除
                        return RemoveNodeFromParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {Location.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"从位置移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将节点添加到父节点的端口
        /// </summary>
        private bool AddNodeToParentPort()
        {
            try
            {
                if (Location.ParentNode == null || string.IsNullOrEmpty(Location.PortName))
                    return false;

                var parentType = Location.ParentNode.GetType();
                var portField = parentType.GetField(Location.PortName) ??
                               parentType.GetProperty(Location.PortName)?.GetValue(Location.ParentNode) as System.Reflection.FieldInfo;

                if (portField == null)
                {
                    // 尝试属性
                    var portProperty = parentType.GetProperty(Location.PortName);
                    if (portProperty == null)
                        return false;

                    var portValue = portProperty.GetValue(Location.ParentNode);

                    if (Location.IsMultiPort)
                    {
                        // 多端口：添加到列表
                        if (portValue is System.Collections.IList list)
                        {
                            if (Location.ListIndex >= 0 && Location.ListIndex <= list.Count)
                            {
                                list.Insert(Location.ListIndex, Node);
                            }
                            else
                            {
                                list.Add(Node);
                            }
                            return true;
                        }
                    }
                    else
                    {
                        // 单端口：直接设置
                        portProperty.SetValue(Location.ParentNode, Node);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"添加节点到父端口失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从父节点的端口移除节点
        /// </summary>
        private bool RemoveNodeFromParentPort()
        {
            try
            {
                if (Location.ParentNode == null || string.IsNullOrEmpty(Location.PortName))
                    return false;

                var parentType = Location.ParentNode.GetType();
                var portProperty = parentType.GetProperty(Location.PortName);
                if (portProperty == null)
                    return false;

                var portValue = portProperty.GetValue(Location.ParentNode);

                if (Location.IsMultiPort)
                {
                    if (portValue is System.Collections.IList list)
                    {
                        list.Remove(Node);
                        return true;
                    }
                }
                else
                {
                    // 单端口：设置为null
                    portProperty.SetValue(Location.ParentNode, null);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"从父端口移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建ViewNode
        /// </summary>
        private void CreateViewNode()
        {
            try
            {
                if (GraphView.NodeDic.ContainsKey(Node))
                    return; // 已存在

                ViewNode viewNode;
                if (Node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(Node, GraphView);
                }
                else
                {
                    viewNode = new ViewNode(Node, GraphView);
                }

                viewNode.SetPosition(new Rect(Node.Position, Vector2.zero));
                GraphView.ViewNodes.Add(viewNode);
                GraphView.NodeDic.Add(Node, viewNode);
                GraphView.AddElement(viewNode);
            }
            catch (Exception e)
            {
                Debug.LogError($"创建ViewNode失败: {e.Message}");
            }
        }

        /// <summary>
        /// 移除ViewNode
        /// </summary>
        private void RemoveViewNode()
        {
            try
            {
                if (GraphView.NodeDic.TryGetValue(Node, out var viewNode))
                {
                    GraphView.ViewNodes.Remove(viewNode);
                    GraphView.NodeDic.Remove(Node);
                    GraphView.RemoveElement(viewNode);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"移除ViewNode失败: {e.Message}");
            }
        }

        public bool CanUndo() => Node != null && Location != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeCreate: {Node?.GetType().Name} at {Location?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeCreate_{Node?.GetHashCode()}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 节点删除操作 - 实现具体的Execute/Undo逻辑
    /// </summary>
    public class NodeDeleteOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeDelete;
        public DateTime Timestamp { get; private set; }
        public string Description => $"删除节点: {Node?.GetType().Name}";

        public JsonNode Node { get; set; }
        public NodeLocation FromLocation { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        // 保存节点在删除前的连接信息
        private List<EdgeInfo> _savedEdges = new List<EdgeInfo>();

        public NodeDeleteOperation(JsonNode node, NodeLocation fromLocation, TreeNodeGraphView graphView)
        {
            Node = node;
            FromLocation = fromLocation;
            GraphView = graphView;
            Timestamp = DateTime.Now;

            // 删除前保存边连接信息
            SaveEdgeConnections();
        }

        /// <summary>
        /// 执行节点删除操作 - 从指定位置移除节点
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null || FromLocation == null || GraphView == null)
                {
                    Debug.LogError("NodeDeleteOperation.Execute: 参数不完整");
                    return false;
                }

                // 保存边连接（如果还没保存）
                if (_savedEdges.Count == 0)
                {
                    SaveEdgeConnections();
                }

                // 移除ViewNode
                RemoveViewNode();

                // 从位置移除节点
                bool success = RemoveNodeFromLocation();

                if (success)
                {
                    Debug.Log($"成功执行节点删除: {Node.GetType().Name} from {FromLocation.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"执行节点删除操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销节点删除操作 - 将节点恢复到原位置
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null || FromLocation == null || GraphView == null)
                {
                    Debug.LogError("NodeDeleteOperation.Undo: 参数不完整");
                    return false;
                }

                // 将节点添加回原位置
                bool success = AddNodeToLocation();

                if (success)
                {
                    // 创建对应的ViewNode
                    CreateViewNode();

                    // 恢复边连接
                    RestoreEdgeConnections();

                    Debug.Log($"成功撤销节点删除: {Node.GetType().Name} at {FromLocation.GetFullPath()}");
                }

                return success;
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销节点删除操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存节点的边连接信息
        /// </summary>
        private void SaveEdgeConnections()
        {
            try
            {
                _savedEdges.Clear();

                // 保存作为子节点的连接（父节点指向此节点）
                var asset = GraphView.Window.JsonAsset;
                if (asset != null)
                {
                    SaveIncomingEdges(asset.Data.Nodes);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"保存边连接信息失败: {e.Message}");
            }
        }

        /// <summary>
        /// 递归保存输入边
        /// </summary>
        private void SaveIncomingEdges(List<JsonNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node == null) continue;

                var nodeType = node.GetType();
                var properties = nodeType.GetProperties();

                foreach (var prop in properties)
                {
                    var value = prop.GetValue(node);

                    // 检查单个子节点连接
                    if (value == Node)
                    {
                        _savedEdges.Add(new EdgeInfo
                        {
                            ParentNode = node,
                            ChildNode = Node,
                            PortName = prop.Name,
                            IsMultiPort = false,
                            ListIndex = -1
                        });
                    }
                    // 检查多个子节点连接
                    else if (value is System.Collections.IList list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] == Node)
                            {
                                _savedEdges.Add(new EdgeInfo
                                {
                                    ParentNode = node,
                                    ChildNode = Node,
                                    PortName = prop.Name,
                                    IsMultiPort = true,
                                    ListIndex = i
                                });
                            }
                        }
                    }
                }

                // 递归检查子节点
                SaveIncomingEdgesFromNode(node);
            }
        }

        /// <summary>
        /// 递归保存输入边
        /// </summary>
        private void SaveIncomingEdgesFromNode(JsonNode node)
        {
            var nodeType = node.GetType();
            var properties = nodeType.GetProperties();

            foreach (var prop in properties)
            {
                var value = prop.GetValue(node);

                if (value is JsonNode childNode && childNode != null)
                {
                    SaveIncomingEdges(new List<JsonNode> { childNode });
                }
                else if (value is System.Collections.IList list)
                {
                    var childNodes = new List<JsonNode>();
                    foreach (var item in list)
                    {
                        if (item is JsonNode child)
                            childNodes.Add(child);
                    }
                    if (childNodes.Count > 0)
                        SaveIncomingEdges(childNodes);
                }
            }
        }

        /// <summary>
        /// 恢复边连接
        /// </summary>
        private void RestoreEdgeConnections()
        {
            try
            {
                foreach (var edge in _savedEdges)
                {
                    var parentType = edge.ParentNode.GetType();
                    var portProperty = parentType.GetProperty(edge.PortName);

                    if (portProperty == null) continue;

                    if (edge.IsMultiPort)
                    {
                        var list = portProperty.GetValue(edge.ParentNode) as System.Collections.IList;
                        if (list != null)
                        {
                            if (edge.ListIndex >= 0 && edge.ListIndex <= list.Count)
                            {
                                list.Insert(edge.ListIndex, Node);
                            }
                            else
                            {
                                list.Add(Node);
                            }
                        }
                    }
                    else
                    {
                        portProperty.SetValue(edge.ParentNode, Node);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"恢复边连接失败: {e.Message}");
            }
        }

        /// <summary>
        /// 将节点添加到位置（复用NodeCreateOperation的逻辑）
        /// </summary>
        private bool AddNodeToLocation()
        {
            // 创建临时的NodeCreateOperation来复用逻辑
            var createOp = new NodeCreateOperation(Node, FromLocation, GraphView);
            return createOp.Execute();
        }

        /// <summary>
        /// 从位置移除节点
        /// </summary>
        private bool RemoveNodeFromLocation()
        {
            try
            {
                var asset = GraphView.Window.JsonAsset;
                if (asset == null)
                    return false;

                switch (FromLocation.Type)
                {
                    case LocationType.Root:
                        return asset.Data.Nodes.Remove(Node);

                    case LocationType.Child:
                        return RemoveNodeFromParentPort();

                    default:
                        Debug.LogWarning($"不支持的位置类型: {FromLocation.Type}");
                        return false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"从位置移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从父节点的端口移除节点
        /// </summary>
        private bool RemoveNodeFromParentPort()
        {
            try
            {
                if (FromLocation.ParentNode == null || string.IsNullOrEmpty(FromLocation.PortName))
                    return false;

                var parentType = FromLocation.ParentNode.GetType();
                var portProperty = parentType.GetProperty(FromLocation.PortName);
                if (portProperty == null)
                    return false;

                var portValue = portProperty.GetValue(FromLocation.ParentNode);

                if (FromLocation.IsMultiPort)
                {
                    if (portValue is System.Collections.IList list)
                    {
                        list.Remove(Node);
                        return true;
                    }
                }
                else
                {
                    portProperty.SetValue(FromLocation.ParentNode, null);
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"从父端口移除节点失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 创建ViewNode
        /// </summary>
        private void CreateViewNode()
        {
            try
            {
                if (GraphView.NodeDic.ContainsKey(Node))
                    return;

                ViewNode viewNode;
                if (Node.PrefabData != null)
                {
                    viewNode = new PrefabViewNode(Node, GraphView);
                }
                else
                {
                    viewNode = new ViewNode(Node, GraphView);
                }

                viewNode.SetPosition(new Rect(Node.Position, Vector2.zero));
                GraphView.ViewNodes.Add(viewNode);
                GraphView.NodeDic.Add(Node, viewNode);
                GraphView.AddElement(viewNode);
            }
            catch (Exception e)
            {
                Debug.LogError($"创建ViewNode失败: {e.Message}");
            }
        }

        /// <summary>
        /// 移除ViewNode
        /// </summary>
        private void RemoveViewNode()
        {
            try
            {
                if (GraphView.NodeDic.TryGetValue(Node, out var viewNode))
                {
                    GraphView.ViewNodes.Remove(viewNode);
                    GraphView.NodeDic.Remove(Node);
                    GraphView.RemoveElement(viewNode);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"移除ViewNode失败: {e.Message}");
            }
        }

        public bool CanUndo() => Node != null && FromLocation != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeDelete: {Node?.GetType().Name} from {FromLocation?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeDelete_{Node?.GetHashCode()}_{Timestamp.Ticks}";
        }

        /// <summary>
        /// 边连接信息
        /// </summary>
        private class EdgeInfo
        {
            public JsonNode ParentNode { get; set; }
            public JsonNode ChildNode { get; set; }
            public string PortName { get; set; }
            public bool IsMultiPort { get; set; }
            public int ListIndex { get; set; }
        }
    }

    /// <summary>
    /// 节点移动操作
    /// </summary>
    public class NodeMoveOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.NodeMove;
        public DateTime Timestamp { get; private set; }
        public string Description => $"移动节点: {Node?.GetType().Name}";

        public JsonNode Node { get; set; }
        public NodeLocation FromLocation { get; set; }
        public NodeLocation ToLocation { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public NodeMoveOperation(JsonNode node, NodeLocation fromLocation, NodeLocation toLocation, TreeNodeGraphView graphView)
        {
            Node = node;
            FromLocation = fromLocation;
            ToLocation = toLocation;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            // 撤销移动操作 - 移回原位置
            return true;
        }

        public bool CanUndo() => Node != null && FromLocation != null && ToLocation != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"NodeMove: {Node?.GetType().Name} from {FromLocation?.GetFullPath()} to {ToLocation?.GetFullPath()}";
        }

        public string GetOperationId()
        {
            return $"NodeMove_{Node?.GetHashCode()}_{FromLocation?.GetFullPath()}_{ToLocation?.GetFullPath()}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 字段修改操作 - 实现具体的Execute/Undo逻辑
    /// </summary>
    public class FieldModifyOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.FieldModify;
        public DateTime Timestamp { get; private set; }

        // 🔥 支持自定义描述信息
        private string _description;
        public string Description
        {
            get => _description ?? $"修改字段: {FieldPath}";
        }

        public JsonNode Node { get; set; }
        public string FieldPath { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public FieldModifyOperation(JsonNode node, string fieldPath, string oldValue, string newValue, TreeNodeGraphView graphView)
        {
            Node = node;
            FieldPath = fieldPath;
            OldValue = oldValue;
            NewValue = newValue;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        /// <summary>
        /// 设置自定义描述信息 - 用于合并操作
        /// </summary>
        public void SetDescription(string description)
        {
            _description = description;
        }

        /// <summary>
        /// 执行字段修改操作 - 将字段设置为新值
        /// </summary>
        public bool Execute()
        {
            try
            {
                if (Node == null)
                {
                    Debug.LogError("FieldModifyOperation.Execute: Node为空");
                    return false;
                }

                return ApplyFieldValue(NewValue);
            }
            catch (Exception e)
            {
                Debug.LogError($"执行字段修改操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 撤销字段修改操作 - 将字段恢复为旧值
        /// </summary>
        public bool Undo()
        {
            try
            {
                if (Node == null)
                {
                    Debug.LogError("FieldModifyOperation.Undo: Node为空");
                    return false;
                }

                return ApplyFieldValue(OldValue);
            }
            catch (Exception e)
            {
                Debug.LogError($"撤销字段修改操作失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 应用字段值 - 核心逻辑，支持各种字段类型
        /// </summary>
        private bool ApplyFieldValue(string value)
        {
            try
            {
                // 处理Position字段的特殊情况
                if (FieldPath == "Position")
                {
                    return ApplyPositionValue(value);
                }

                // 🔥 修复：使用JsonNode的本地字段路径而不是全局路径
                // 通过反射设置字段值 - 使用Node.SetValue方法更准确
                return ApplyFieldValueViaJsonNode(value);
            }
            catch (Exception e)
            {
                Debug.LogError($"应用字段值失败 {FieldPath} = {value}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 应用Position字段值
        /// </summary>
        private bool ApplyPositionValue(string value)
        {
            try
            {
                // 解析Position字符串，格式："(x, y)"
                if (TryParsePosition(value, out var position))
                {
                    Node.Position = position;

                    // 同步更新ViewNode的位置
                    if (GraphView?.NodeDic.TryGetValue(Node, out var viewNode) == true)
                    {
                        viewNode.SetPosition(new Rect(position, Vector2.zero));
                    }

                    return true;
                }

                Debug.LogWarning($"无法解析Position值: {value}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"应用Position值失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 解析Position字符串
        /// </summary>
        private bool TryParsePosition(string positionStr, out Vec2 position)
        {
            position = default;

            if (string.IsNullOrEmpty(positionStr))
                return false;

            // 移除括号和空格
            positionStr = positionStr.Trim('(', ')', ' ');
            var parts = positionStr.Split(',');

            if (parts.Length != 2)
                return false;

            if (float.TryParse(parts[0].Trim(), out var x) &&
                float.TryParse(parts[1].Trim(), out var y))
            {
                position = new Vec2(x, y);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 🔥 新增：通过JsonNode的本地路径应用字段值 - 更准确的实现
        /// </summary>
        private bool ApplyFieldValueViaJsonNode(string value)
        {
            try
            {
                // 使用JsonNode的SetValue方法，支持本地字段路径  
                var convertedValue = ConvertStringToValue(value);
                Node.SetValue(FieldPath, convertedValue);
                return true; // SetValue方法返回void，成功执行即返回true
            }
            catch (Exception e)
            {
                Debug.LogError($"通过JsonNode设置字段值失败: {e.Message}");
                // 如果JsonNode.SetValue失败，回退到反射方式
                return ApplyFieldValueViaReflection(value);
            }
        }

        /// <summary>
        /// 🔥 优化：将字符串转换为合适的值类型 - 简化版本
        /// </summary>
        private object ConvertStringToValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // 尝试从Node获取字段类型进行更准确的转换
            try
            {
                var currentValue = Node.GetValue<object>(FieldPath);
                if (currentValue != null)
                {
                    var targetType = currentValue.GetType();
                    return ConvertStringToFieldType(value, targetType);
                }
            }
            catch
            {
                // 获取当前值失败，使用通用转换
            }

            // 通用类型推断和转换
            return ConvertStringToGenericType(value);
        }

        /// <summary>
        /// 通用字符串到类型转换
        /// </summary>
        private object ConvertStringToGenericType(string value)
        {
            // 尝试常见类型转换
            if (bool.TryParse(value, out var boolValue))
                return boolValue;
            if (int.TryParse(value, out var intValue))
                return intValue;
            if (float.TryParse(value, out var floatValue))
                return floatValue;
            if (double.TryParse(value, out var doubleValue))
                return doubleValue;

            // 默认返回字符串
            return value;
        }

        /// <summary>
        /// 通过反射应用字段值 - 保留作为后备方案
        /// </summary>
        private bool ApplyFieldValueViaReflection(string value)
        {
            try
            {
                var nodeType = Node.GetType();
                var fieldInfo = nodeType.GetField(FieldPath,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (fieldInfo != null)
                {
                    var convertedValue = ConvertStringToFieldType(value, fieldInfo.FieldType);
                    fieldInfo.SetValue(Node, convertedValue);
                    return true;
                }

                var propertyInfo = nodeType.GetProperty(FieldPath,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    var convertedValue = ConvertStringToFieldType(value, propertyInfo.PropertyType);
                    propertyInfo.SetValue(Node, convertedValue);
                    return true;
                }

                Debug.LogWarning($"未找到字段或属性: {FieldPath} 在类型 {nodeType.Name} 中");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"反射设置字段值失败: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 将字符串转换为对应的字段类型
        /// </summary>
        private object ConvertStringToFieldType(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return GetDefaultValue(targetType);

            // 处理可空类型
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            try
            {
                if (underlyingType == typeof(string))
                    return value;
                if (underlyingType == typeof(int))
                    return int.Parse(value);
                if (underlyingType == typeof(float))
                    return float.Parse(value);
                if (underlyingType == typeof(double))
                    return double.Parse(value);
                if (underlyingType == typeof(bool))
                    return bool.Parse(value);
                if (underlyingType.IsEnum)
                    return Enum.Parse(underlyingType, value);

                // 使用Convert.ChangeType作为后备方案
                return Convert.ChangeType(value, underlyingType);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"类型转换失败 {value} -> {targetType}: {e.Message}");
                return GetDefaultValue(targetType);
            }
        }

        /// <summary>
        /// 获取类型的默认值
        /// </summary>
        private object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public bool CanUndo() => Node != null && !string.IsNullOrEmpty(FieldPath) && GraphView != null;

        public string GetOperationSummary()
        {
            return $"FieldModify: {FieldPath} from '{OldValue}' to '{NewValue}'";
        }

        public string GetOperationId()
        {
            // 🔥 优化操作ID生成 - 使用本地字段路径，确保同一节点同一字段的操作能被识别为同类操作进行合并
            // 这样连续的字段变化操作会有相同的操作ID前缀，便于合并逻辑识别
            return $"FieldModify_{Node?.GetHashCode()}_{FieldPath}";
        }
    }

    /// <summary>
    /// 边连接操作
    /// </summary>
    public class EdgeCreateOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.EdgeCreate;
        public DateTime Timestamp { get; private set; }
        public string Description => "创建边连接";

        public JsonNode ParentNode { get; set; }
        public JsonNode ChildNode { get; set; }
        public string PortName { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public EdgeCreateOperation(JsonNode parentNode, JsonNode childNode, string portName, TreeNodeGraphView graphView)
        {
            ParentNode = parentNode;
            ChildNode = childNode;
            PortName = portName;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            return true;
        }

        public bool CanUndo() => ParentNode != null && ChildNode != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"EdgeCreate: {ParentNode?.GetType().Name}.{PortName} -> {ChildNode?.GetType().Name}";
        }

        public string GetOperationId()
        {
            return $"EdgeCreate_{ParentNode?.GetHashCode()}_{ChildNode?.GetHashCode()}_{PortName}_{Timestamp.Ticks}";
        }
    }

    /// <summary>
    /// 边断开操作
    /// </summary>
    public class EdgeRemoveOperation : IAtomicOperation
    {
        public OperationType Type => OperationType.EdgeRemove;
        public DateTime Timestamp { get; private set; }
        public string Description => "断开边连接";

        public JsonNode ParentNode { get; set; }
        public JsonNode ChildNode { get; set; }
        public string PortName { get; set; }
        public TreeNodeGraphView GraphView { get; set; }

        public EdgeRemoveOperation(JsonNode parentNode, JsonNode childNode, string portName, TreeNodeGraphView graphView)
        {
            ParentNode = parentNode;
            ChildNode = childNode;
            PortName = portName;
            GraphView = graphView;
            Timestamp = DateTime.Now;
        }

        public bool Execute()
        {
            return true;
        }

        public bool Undo()
        {
            return true;
        }

        public bool CanUndo() => ParentNode != null && ChildNode != null && GraphView != null;

        public string GetOperationSummary()
        {
            return $"EdgeRemove: {ParentNode?.GetType().Name}.{PortName} -X-> {ChildNode?.GetType().Name}";
        }

        public string GetOperationId()
        {
            return $"EdgeRemove_{ParentNode?.GetHashCode()}_{ChildNode?.GetHashCode()}_{PortName}_{Timestamp.Ticks}";
        }
    }
}
