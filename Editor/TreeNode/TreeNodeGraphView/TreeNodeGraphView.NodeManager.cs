using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = TreeNode.Utility.Debug;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//节点管理系统
    {
        public List<ViewNode> ViewNodes;
        public Dictionary<JsonNode, ViewNode> NodeDic;

        #region 节点管理

        public virtual void AddNode(JsonNode node)
        {
            PAPath path = PAPath.Index(Asset.Data.Nodes.Count);
            Asset.Data.Nodes.Add(node);
            
            var createOperation =  NodeOperation.Create(node, path, Asset);
            Window.History.Record(createOperation);

            //NodeTree.OnNodeAdded(node, path);
            AddViewNode(node);
        }

        public bool SetNodeByPath(JsonNode node, PAPath path)
        {
            if (path.IsEmpty)
            {
                path = PAPath.Index(Asset.Data.Nodes.Count);
                Asset.Data.Nodes.Add(node);
                var createOperation = NodeOperation.Create(node, path, this.Asset);
                Window.History.Record(createOperation);
                //NodeTree.OnNodeAdded(node, path);
                return true;
            }
            try
            {
                PAPath path_ = path;
                PAPath parentPath = path_.GetParent();
                int index = 0;
                object parent = Asset.Data.Nodes.GetValueInternal<object>(ref parentPath, ref index);
                PAPart last = path_.LastPart;
                object oldValue = PropertyAccessor.GetValue<object>(parent, last);
                
                bool add = false;
                if (oldValue is null)
                {
                    Type parentType = parent.GetType();
                    Type valueType = TypeCacheSystem.GetTypeInfo(parentType).GetMember(last.Name).ValueType;
                    if (valueType.Inherited(typeof(IList)))
                    {
                        oldValue = Activator.CreateInstance(valueType);
                        PropertyAccessor.SetValue(parent, last, oldValue);
                    }
                }
                else if (oldValue is IList nodeList)
                {
                    path_ = path_.Append(nodeList.Count);
                    nodeList.Add(node);
                    add = true;
                }
                if (!add)
                {
                    PAPath lastPath = new(last);
                    int index_ = 0;
                    if (parent is IPropertyAccessor accessor)
                    {
                        accessor.SetValueInternal(ref lastPath, ref index_, node);
                    }
                    else if (parent is IList list)
                    {
                        list.SetValueInternal(ref lastPath, ref index_, node);
                    }
                    else
                    {
                        PropertyAccessor.SetValueInternal(parent, ref lastPath, ref index_, node);
                    }
                }
                var moveOperation = NodeOperation.Create(node, path_, this.Asset);
                Window.History.Record(moveOperation);
                //NodeTree.RebuildTree();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error setting node by path: {e}");
                return false;
            }
        }

        void RemoveNode(JsonNode node)
        {
            // 记录节点删除操作
            int index = Asset.Data.Nodes.IndexOf(node);
            PAPath path = PAPath.Index(index);

            var deleteOperation = NodeOperation.Delete(node, path, this.Asset);
            Window.History.Record(deleteOperation);
            
            Asset.Data.Nodes.Remove(node);
            //NodeTree.OnNodeRemoved(node,path);
        }

        public ViewNode AddViewNode(JsonNode node)
        {
            if (NodeDic.TryGetValue(node, out ViewNode viewNode)) { return viewNode; }

            if (node.TemplateData != null)
            {
                viewNode = new TemplateNode(node, this);
            }
            else
            {
                viewNode = new ViewNode(node, this);
            }
            viewNode.SetPosition(new Rect(node.Position, new()));
            ViewNodes.Add(viewNode);
            NodeDic.Add(node, viewNode);
            AddElement(viewNode);
            return viewNode;
        }

        /// <summary>
        /// 专用于工具添加节点的方法
        /// </summary>
        public ViewNode AddViewNodeWithConnection(JsonNode node, PAPath nodePath)
        {
            // 1. 创建ViewNode（使用现有的AddViewNode方法）
            ViewNode viewNode = AddViewNode(node);
            
            // 2. 如果指定了路径，尝试创建连接
            if (!string.IsNullOrEmpty(nodePath))
            {
                CreateToolNodeConnection(viewNode, nodePath);
            }
            
            return viewNode;
        }

        /// <summary>
        /// 为工具添加的节点创建连接 - 优化版本
        /// </summary>
        private void CreateToolNodeConnection(ViewNode childViewNode, PAPath nodePath)
        {
            try
            {
                // 通过现有的GetPort方法查找端口
                var childPort = GetPort(nodePath);
                //Debug.Log(childPort);
                if (childPort != null && childViewNode.ParentPort != null)
                {
                    // 立即创建连接
                    CreateConnectionImmediately(childPort, childViewNode);
                }
                else
                {
                    Debug.LogError($"工具节点连接失败: 找不到端口 - 路径: {nodePath}");
                    
                    // 尝试延迟连接创建 - 可能端口还未创建完成
                    schedule.Execute(() => 
                    {
                        var retryPort = GetPort(nodePath);
                        if (retryPort != null && childViewNode.ParentPort != null)
                        {
                            CreateConnectionImmediately(retryPort, childViewNode);
                            Debug.Log($"延迟重试成功创建工具节点连接: {nodePath}");
                        }
                    }).ExecuteLater(100); // 100ms后重试
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"工具节点连接创建失败: {e.Message}");
            }
        }

        /// <summary>
        /// 立即创建连接
        /// </summary>
        private void CreateConnectionImmediately(ChildPort childPort, ViewNode childViewNode)
        {
            var edge = childPort.ConnectTo(childViewNode.ParentPort);
            AddElement(edge);
            
            // 处理多端口索引 - 优化索引计算
            if (childPort is MultiPort multiPort)
            {
                var childValues = multiPort.GetChildValues();
                int newIndex = Math.Max(0, childValues.Count - 1);
                childViewNode.ParentPort.SetIndex(newIndex);
                //Debug.Log($"设置MultiPort索引: {newIndex}");
            }
            
            //Debug.Log($"立即创建工具节点连接: {childPort.node.Data.GetType().Name} -> {childViewNode.Data.GetType().Name}");
        }

        public virtual void RemoveViewNode(ViewNode node)
        {
            RemoveNode(node.Data);
            ViewNodes.Remove(node);
            NodeDic.Remove(node.Data);
        }

        #endregion
        #region Edge连接管理
        public virtual void CreateEdge(Edge edge)
        {
            //Debug.Log("CreateEdge");
            ViewNode childNode = edge.ParentPort().node;
            ViewNode parentNode = edge.ChildPort().node;
            PAPath from = PAPath.Index(Asset.Data.Nodes.IndexOf(childNode.Data));
            Asset.Data.Nodes.Remove(childNode.Data);
            ChildPort childPortOfParentNode = edge.ChildPort();
            PAPath to = parentNode.GetNodePath().Combine(childPortOfParentNode.SetNodeValue(childNode.Data, false));
            Window.History.Record(NodeOperation.Move(childNode.Data, from, to, this.Asset));
            edge.ParentPort().Connect(edge);
            childPortOfParentNode.Connect(edge);
            childPortOfParentNode.OnAddEdge(edge);
        }

        public virtual void RemoveEdge(Edge edge)
        {

            ViewNode parent = edge.ChildPort()?.node;
            ViewNode child = edge.ParentPort()?.node;
            if (parent == null || child == null) { return; }
            PAPath from = child.GetNodePath();
            //Debug.Log($"RemoveEdge: {from}");
            ChildPort childPortOfParent = edge.ChildPort();
            childPortOfParent.SetNodeValue(child.Data);
            Asset.Data.Nodes.Add(child.Data);
            Window.History.Record(NodeOperation.Move(child.Data, from, PAPath.Index(Asset.Data.Nodes.Count - 1), this.Asset));
            edge.ParentPort().DisconnectAll();
            childPortOfParent.OnRemoveEdge(edge);
        }
        #endregion
        #region 端口兼容性检查

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
                    allPorts.AddRange(node.ChildPorts.Values);
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

        #endregion
        #region 数据访问和查询
        public ChildPort GetPort(PAPath path)
        {
            if (path.ItemOfCollection)
            { 
                path = path.GetParent();
            }
            if (path.IsEmpty)
            {
                return null;
            }
            PAPath parent = path.GetParent();
            int index = 0;
            List<(int, JsonNode)> list = ListPool<(int, JsonNode)>.GetList();
            Asset.Data.Nodes.GetAllInPath(ref parent, ref index, list);
            JsonNode parentNode = list.Last().Item2;
            PAPath local = path.GetSubPath(list.Last().Item1+1);
            list.Release();
            return NodeDic.TryGetValue(parentNode, out ViewNode viewNode) ? viewNode.GetChildPort(local): null;
        }

        /// <summary>
        /// 验证 - 使用逻辑层验证
        /// </summary>
        public virtual string Validate(bool appendText = true)
        {
            string text =  Asset?.Data?.GetTreeView((node) =>
            {
                ViewNode viewNode = NodeDic.GetValueOrDefault(node);
                ValidationResult res = viewNode.Validate(out string error);
                if (res == ValidationResult.Success)
                {
                    return $"{node.GetInfo()} ✔︎";
                }
                else
                {
                    string symbol = res switch
                    {
                        ValidationResult.Warning => "⚠️",
                        ValidationResult.Failure => "✘",
                        _ => ""
                    };
                    return $"{node.GetInfo()} {symbol} - {error}";
                }
            }, appendText);
            if (text.Contains("⚠️"))
            {
                text = $"⚠️警告不代表错误,需仔细甄别该配置是否符合实际设计意图,如符合需求,可忽略警告\n{text}";
            }
            return text;
        }

        /// <summary>
        /// 获取所有节点路径 - 使用逻辑层实现
        /// </summary>
        public virtual List<(string, string)> GetAllNodePaths() => Asset?.Data.GetAllNodeInfo();
        #endregion
        #region 图表视图管理
        private GraphViewChange OnGraphViewChanged(GraphViewChange graphViewChange)
        {
            //Debug.Log($"启动批量操作");
            Window.History.BeginBatch();
            // 处理删除操作
            if (graphViewChange.elementsToRemove != null && graphViewChange.elementsToRemove.Count > 0)
            {
                ProcessRemoveOperations(graphViewChange.elementsToRemove.OfType<ViewNode>().ToList(),
                    graphViewChange.elementsToRemove.OfType<Edge>().ToList(),
                    graphViewChange);
            }
            // 处理创建操作
            if (graphViewChange.edgesToCreate != null&& graphViewChange.edgesToCreate.Count > 0)
            {
                ProcessCreateOperations(graphViewChange.edgesToCreate.ToList());
            }
            if (graphViewChange.movedElements != null && graphViewChange.movedElements.Count > 0&& !graphViewChange.moveDelta.Equals(Vector2.zero))
            {
                ProcessNodeMoveOperations(graphViewChange.movedElements.OfType<ViewNode>().ToList(), graphViewChange.moveDelta);
            }
            //Debug.Log($"结束批量操作");
            Window.History.EndBatch();
            return graphViewChange;
        }
        private void ProcessRemoveOperations(List<ViewNode> nodesToRemove, List<Edge> edgesToRemove, 
            GraphViewChange graphViewChange)
        {
            //Debug.Log($"开始处理删除操作 - 节点:{nodesToRemove.Count}个, 直接删除边:{edgesToRemove.Count}个");
            
            // 1. 首先收集所有需要删除的边（包括节点关联的边)
            var allEdgesToRemove = new HashSet<Edge>(edgesToRemove);
            int nodeAssociatedEdges = 0;
            
            foreach (ViewNode viewNode in nodesToRemove)
            {
                var nodeEdges = viewNode.GetAllEdges();
                foreach (var edge in nodeEdges)
                {
                    if (allEdgesToRemove.Add(edge)) // Add方法返回true表示是新添加的
                    {
                        nodeAssociatedEdges++;
                    }
                }
            }
            
            //Debug.Log($"收集到节点关联边:{nodeAssociatedEdges}个, 总删除边数:{allEdgesToRemove.Count}个");
            
            // 2. 按照依赖关系顺序删除边
            var edgeList = allEdgesToRemove.ToList();
            ProcessEdgeRemovalInOrder(edgeList);
            
            // 3. 删除节点（按深度倒序，避免引用问题)
            var sortedNodes = nodesToRemove.OrderByDescending(n => n.GetDepth()).ToList();
            //Debug.Log($"按深度排序删除节点，顺序: {string.Join(" -> ", sortedNodes.Select(n => $"{n.Data.GetType().Name}(深度{n.GetDepth()})"))}");
            
            foreach (ViewNode viewNode in sortedNodes)
            {
                //Debug.Log($"删除节点: {viewNode.Data.GetType().Name}");
                RemoveViewNode(viewNode);
            }
            
            // 4. 更新graphViewChange以包含所有边
            graphViewChange.elementsToRemove.Clear();
            graphViewChange.elementsToRemove.AddRange(allEdgesToRemove);
            graphViewChange.elementsToRemove.AddRange(nodesToRemove);
            
            //Debug.Log($"删除操作完成 - 实际删除边:{allEdgesToRemove.Count}个, 节点:{nodesToRemove.Count}个");
        }
        private void ProcessEdgeRemovalInOrder(List<Edge> edges)
        {
            // 按连接深度排序，先删除深层的边，再删除浅层的边
            var sortedEdges = edges
                .Where(e => e?.ParentPort()?.node != null)
                .OrderByDescending(e => e.ParentPort().node.GetDepth())
                .ToList();
            
            //Debug.Log($"按深度排序删除 {sortedEdges.Count} 条边");
            
            foreach (Edge edge in sortedEdges)
            {
                try
                {
                    var parentNode = edge.ChildPort()?.node?.Data?.GetType().Name ?? "Unknown";
                    var childNode = edge.ParentPort()?.node?.Data?.GetType().Name ?? "Unknown";
                    //Debug.Log($"删除边: {parentNode} -> {childNode}");
                    RemoveEdge(edge);
                }
                catch (Exception e)
                {
                    Debug.LogError($"删除边时出错: {e.Message}");
                }
            }
        }
        private void ProcessCreateOperations(List<Edge> edgesToCreate)
        {
            // 按源节点深度排序，确保创建顺序正确
            var sortedEdges = edgesToCreate
                .Where(e => e?.ChildPort()?.node != null)
                .OrderBy(e => e.ChildPort().node.GetDepth())
                .ToList();
            
            foreach (Edge edge in sortedEdges)
            {
                try
                {
                    CreateEdge(edge);
                }
                catch (Exception e)
                {
                    Debug.LogError($"创建边时出错: {e.Message}");
                }
            }
        }
        private void ProcessNodeMoveOperations(List<ViewNode> viewNodes, Vector2 delta)
        {
            for (int i = 0; i < viewNodes.Count; i++)
            {
                ViewNode viewNode = viewNodes[i];
                try
                {
                    Vec2 from = viewNode.Data.Position;
                    // 更新逻辑层数据
                    viewNode.Data.Position += (Vec2)delta;
                    Vec2 to = viewNode.Data.Position;
                    // 记录节点移动操作
                    var moveOperation = new FieldModifyOperation<Vec2>(viewNode.Data, PAPath.Position, from, to);
                    Window.History.Record(moveOperation);
                }
                catch (Exception e)
                {
                    Debug.LogError($"移动节点时出错: {e.Message}");
                }
            }
        }
        #endregion

    }
}
