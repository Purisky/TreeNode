using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//NodeOp
    {
        public JsonAsset Asset => Window.JsonAsset;
        public List<ViewNode> ViewNodes;
        public Dictionary<JsonNode, ViewNode> NodeDic;

        // 逻辑层树结构处理器 - 改为立即初始化
        private JsonNodeTree _nodeTree;
        public JsonNodeTree NodeTree => _nodeTree;
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> allPorts = new();
            List<Port> ports = new();

            foreach (var node in ViewNodes)
            {
                if (node == startPort.node) { continue; }

                if (startPort.direction == Direction.Output)
                {
                    if (node.ParentPort != null)
                    {
                        allPorts.Add(node.ParentPort);
                    }
                }
                else
                {
                    allPorts.AddRange(node.ChildPorts);
                }
            }

            ports = allPorts.Where(x => CheckPort(startPort, x)).ToList();
            return ports;
        }

        public virtual bool CheckPort(Port start, Port end)
        {
            if (start.portType != end.portType) { return false; }
            ParentPort parentPort;
            ChildPort childPort;
            if (start is ParentPort)
            {
                parentPort = start as ParentPort;
                childPort = end as ChildPort;
            }
            else
            {
                parentPort = end as ParentPort;
                childPort = start as ChildPort;
            }
            return CheckMulti(parentPort, childPort) && CheckLoop(childPort.node, parentPort.node);
        }

        public bool CheckMulti(ParentPort parentPort, ChildPort childPort)
        {
            return !parentPort.Collection || childPort is MultiPort;
        }

        public bool CheckLoop(ViewNode parent, ViewNode child)
        {
            ViewNode node = parent;
            while (node != null)
            {
                if (node == child) { return false; }
                node = node.GetParent();
            }
            return true;
        }

        public virtual void CreateEdge(Edge edge)
        {
            //Debug.Log("CreateEdge");
            ViewNode childNode = edge.ParentPort().node;
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(childNode.Data, false);
            edge.ParentPort().Connect(edge);
            childport_of_parent.Connect(edge);
            childport_of_parent.OnAddEdge(edge);
        }
        public virtual void RemoveEdge(Edge edge)
        {
            ViewNode parent = edge.ChildPort()?.node;
            ViewNode child = edge.ParentPort()?.node;
            if (parent == null || child == null) { return; }
            ChildPort childport_of_parent = edge.ChildPort();
            childport_of_parent.SetNodeValue(child.Data);
            Asset.Data.Nodes.Add(child.Data);
            edge.ParentPort().DisconnectAll();
            childport_of_parent.OnRemoveEdge(edge);
        }

        public virtual void AddNode(JsonNode node)
        {
            Asset.Data.Nodes.Add(node);
            NodeTree.OnNodeAdded(node);
            // 使用增量历史记录节点添加操作
            Window.History.RecordAddNode(node);
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Asset.Data.Nodes.Add(node);
                NodeTree.OnNodeAdded(node);
                return true;
            }
            try
            {
                object parent = PropertyAccessor.GetParentObject(Asset.Data.Nodes, path, out string last);
                object oldValue = PropertyAccessor.GetValue<object>(parent, last);
                if (oldValue is null)
                {
                    Type parentType = parent.GetType();
                    Type valueType = parentType.GetMember(last).First().GetValueType();
                    if (valueType.Inherited(typeof(IList)))
                    {
                        oldValue = Activator.CreateInstance(valueType);
                        PropertyAccessor.SetValue(parent, last, oldValue);
                    }
                }
                if (oldValue is IList list)
                {
                    list.Add(node);
                    // 使用增量历史记录节点添加操作（带路径）
                    Window.History.RecordAddNode(node, path);
                }
                else
                {
                    PropertyAccessor.SetValue(parent, last, node);
                    // 使用增量历史记录节点添加操作（带路径）
                    Window.History.RecordAddNode(node, path);
                }
                NodeTree.OnNodeAdded(node, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error setting node by path: {e.Message}");
                return false;
            }
        }

        void RemoveNode(JsonNode node)
        {
            Asset.Data.Nodes.Remove(node);
            NodeTree.OnNodeRemoved(node);
        }

        public ViewNode AddViewNode(JsonNode node, ChildPort childPort = null)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; }

            if (node.PrefabData != null)
            {
                viewNode = new PrefabViewNode(node, this, childPort);
            }
            else
            {
                viewNode = new(node, this, childPort);
            }
            viewNode.SetPosition(new Rect(node.Position, new()));
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);

            viewNode.AddChildNodesUntilListInited();
            return viewNode;
        }


        public virtual void RemoveViewNode(ViewNode node)
        {
            // 使用增强版删除命令，不再需要路径参数
            Window.History.RecordRemoveNode(node.Data);
            RemoveNode(node.Data);
            ViewNodes.Remove(node);
            NodeDic.Remove(node.Data);
        }
        /// <summary>
        /// 在对象中递归查找节点
        /// </summary>
        private string FindNodeInObject(object obj, JsonNode targetNode, string currentPath)
        {
            if (obj == null || ReferenceEquals(obj, targetNode))
                return currentPath;

            var objType = obj.GetType();

            // 检查所有属性和字段
            var members = objType.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.MemberType == MemberTypes.Property || m.MemberType == MemberTypes.Field);

            foreach (var member in members)
            {
                try
                {
                    object value = null;
                    if (member is PropertyInfo prop)
                    {
                        if (prop.CanRead)
                            value = prop.GetValue(obj);
                    }
                    else if (member is FieldInfo field)
                    {
                        value = field.GetValue(obj);
                    }

                    if (value == null) continue;

                    string memberPath = string.IsNullOrEmpty(currentPath) ? member.Name : $"{currentPath}.{member.Name}";

                    // 直接匹配
                    if (ReferenceEquals(value, targetNode))
                    {
                        return memberPath;
                    }

                    // 如果是集合，查找集合中的元素
                    if (value is System.Collections.IEnumerable enumerable && !(value is string))
                    {
                        int index = 0;
                        foreach (var item in enumerable)
                        {
                            if (ReferenceEquals(item, targetNode))
                            {
                                return $"{memberPath}[{index}]";
                            }

                            // 递归查找嵌套对象
                            if (item != null && item.GetType().Namespace?.StartsWith("System") != true)
                            {
                                var result = FindNodeInObject(item, targetNode, $"{memberPath}[{index}]");
                                if (!string.IsNullOrEmpty(result))
                                    return result;
                            }
                            index++;
                        }
                    }
                    // 递归查找复杂对象
                    else if (value.GetType().Namespace?.StartsWith("System") != true)
                    {
                        var result = FindNodeInObject(value, targetNode, memberPath);
                        if (!string.IsNullOrEmpty(result))
                            return result;
                    }
                }
                catch
                {
                    // 跳过无法访问的成员
                }
            }

            return null;
        }
        /// <summary>
        /// 获取所有节点路径 - 使用逻辑层实现
        /// </summary>
        public virtual List<(string, string)> GetAllNodePaths()
        {
            return NodeTree.GetAllNodePaths();
        }

        /// <summary>
        /// 获取端口
        /// </summary>
        public ChildPort GetPort(string path) => Find(path)?.Q<ChildPort>();
        /// <summary>
        /// 根据路径查找PropertyElement
        /// </summary>
        public PropertyElement Find(string path)
        {
            JsonNode node = PropertyAccessor.GetLast<JsonNode>(Asset.Data.Nodes, path, false, out int index);
            if (node is null) { return null; }
            if (index >= path.Length - 1) { return null; }
            string local = path[index..];
            return NodeDic[node]?.FindByLocalPath(local);
        }
        /// <summary>
        /// 根据名称和索引查找ChildPort
        /// </summary>
        private ChildPort FindChildPortByName(ViewNode parentViewNode, string portName, bool isMultiPort, int listIndex)
        {
            foreach (var childPort in parentViewNode.ChildPorts)
            {
                // 通过PropertyElement获取端口路径信息
                var propertyElement = childPort.GetFirstAncestorOfType<PropertyElement>();
                if (propertyElement != null)
                {
                    var memberName = propertyElement.MemberMeta.Path.Split('.').LastOrDefault();
                    if (memberName == portName)
                    {
                        if (isMultiPort && childPort is MultiPort)
                        {
                            return childPort;
                        }
                        else if (!isMultiPort && childPort is not MultiPort)
                        {
                            return childPort;
                        }
                    }
                }
            }
            return null;
        }
        private void RequestNodeCreation(VisualElement target, int index, Vector2 position)
        {
            if (nodeCreationRequest != null)
            {
                Vector2 screenMousePosition = Window.position.position + position;
                nodeCreationRequest(new NodeCreationContext
                {
                    screenMousePosition = screenMousePosition,
                    target = target,
                    index = index
                });
            }
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            // 开始批量操作记录
            var batchCommand = Window.History.BeginBatch();

            if (graphViewChange.elementsToRemove != null)
            {
                IEnumerable<ViewNode> nodes = graphViewChange.elementsToRemove.OfType<ViewNode>().Reverse();
                List<Edge> temp = new();
                if (nodes.Any())
                {
                    foreach (ViewNode viewNode in nodes)
                    {
                        temp.AddRange(viewNode.GetAllEdges());
                        // 记录节点删除操作到批量命令 - 使用增强版删除命令
                        var removeCommand = new EnhancedRemoveNodeCommand(viewNode.Data, Window.JsonAsset);
                        batchCommand.AddCommand(removeCommand);
                    }
                }
                graphViewChange.elementsToRemove.AddRange(temp);
                IEnumerable<Edge> edges = graphViewChange.elementsToRemove.OfType<Edge>().Distinct();
                if (edges.Any())
                {
                    foreach (Edge edge in edges)
                    {
                        RemoveEdge(edge);
                        // 连接变化也可以记录，但这里我们简化处理
                    }
                }
                foreach (ViewNode viewNode in nodes)
                {
                    RemoveNode(viewNode.Data);
                    ViewNodes.Remove(viewNode);
                    NodeDic.Remove(viewNode.Data);
                }
            }
            if (graphViewChange.edgesToCreate != null)
            {
                foreach (Edge edge in graphViewChange.edgesToCreate)
                {
                    CreateEdge(edge);
                    // 连接创建也可以记录，但这里我们简化处理
                }
            }

            // 结束批量操作记录
            Window.History.EndBatch(batchCommand);
            return graphViewChange;
        }

    }
}
