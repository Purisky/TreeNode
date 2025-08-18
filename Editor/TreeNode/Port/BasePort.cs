using System;
using System.Collections.Generic;
using System.Reflection;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public abstract class BasePort : Port
    {

        protected static StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("NodePort");
        
        public new ViewNode node;
        public Action OnChange;
        protected BasePort(ViewNode node_,Direction portDirection, Capacity portCapacity, Type type) : base(Orientation.Horizontal, portDirection, portCapacity, type)
        {
            node = node_;
            EdgeConnectorListener listener = new();
            m_EdgeConnector = new EdgeConnector<Edge>(listener);
            UnityEngine.UIElements.VisualElementExtensions.AddManipulator(this, m_EdgeConnector);
            styleSheets.Add(StyleSheet);
            PortColorAttribute portColorAtt = portType.GetCustomAttribute<PortColorAttribute>();
            if (portColorAtt != null)
            {
                portColor = portColorAtt.Color;
            }
        }

        protected class EdgeConnectorListener : IEdgeConnectorListener
        {
            private GraphViewChange m_GraphViewChange;

            private List<Edge> m_EdgesToCreate;

            private List<GraphElement> m_EdgesToDelete;

            public EdgeConnectorListener()
            {
                m_EdgesToCreate = new List<Edge>();
                m_EdgesToDelete = new List<GraphElement>();
                m_GraphViewChange.edgesToCreate = m_EdgesToCreate;
            }

            public void OnDropOutsidePort(Edge edge, Vector2 position)
            {
                // 检测是否从childPort（输出端口）拖拽
                if (edge.output is ChildPort childPort)
                {
                    // 获取GraphView
                    TreeNodeGraphView graphView = childPort.node.View;
                    if (graphView != null)
                    {
                        // 设置类型过滤和待连接端口
                        graphView.SearchProvider.SetTypeFilter(childPort.portType, childPort);
                        
                        // 计算屏幕坐标
                        Vector2 screenPosition = graphView.Window.position.position + position;
                        
                        // 显示搜索窗口
                        graphView.SearchProvider.Target = null;
                        SearchWindow.Open(new SearchWindowContext(screenPosition), graphView.SearchProvider);
                    }
                }
            }

            public void OnDrop(GraphView graphView, Edge edge)
            {
                m_EdgesToCreate.Clear();
                m_EdgesToCreate.Add(edge);
                m_EdgesToDelete.Clear();
                if (edge.input.capacity == Capacity.Single)
                {
                    foreach (Edge connection in edge.input.connections)
                    {
                        if (connection != edge)
                        {
                            m_EdgesToDelete.Add(connection);
                        }
                    }
                }

                if (edge.output.capacity == Capacity.Single)
                {
                    foreach (Edge connection2 in edge.output.connections)
                    {
                        if (connection2 != edge)
                        {
                            m_EdgesToDelete.Add(connection2);
                        }
                    }
                }

                if (m_EdgesToDelete.Count > 0)
                {
                    graphView.DeleteElements(m_EdgesToDelete);
                }

                List<Edge> edgesToCreate = m_EdgesToCreate;
                if (graphView.graphViewChanged != null)
                {
                    edgesToCreate = graphView.graphViewChanged(m_GraphViewChange).edgesToCreate;
                }

                foreach (Edge item in edgesToCreate)
                {
                    graphView.AddElement(item);
                    edge.input.Connect(item);
                    edge.output.Connect(item);
                }
            }
        }









    }


    public static class PortExtensions
    {
        public static ParentPort ParentPort(this Edge edge)
        { 
            return edge.input as ParentPort;
        }
        public static ChildPort ChildPort(this Edge edge)
        {
            return edge.output as ChildPort;
        }
    }
}
