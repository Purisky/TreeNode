﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView//Format
    {
        const int X_SPACE = 30;
        const int Y_SPACE = 10;
        int[] maxWidthPerDepth;
        int[] validYPosPerDepth;

        public int GetXPos(int depth)
        {
            int x = 0;
            for (int i = 0; i < depth; i++)
            {
                x += maxWidthPerDepth[i] + X_SPACE;
            }
            return x;
        }

        public void FormatNodes()
        {
            if (ViewNodes.Count <= 1) return;
            
            // 初始化数组  
            maxWidthPerDepth = new int[256];
            validYPosPerDepth = new int[256];
            
            // 第一步：计算每个深度层的最大宽度
            for (int i = 0; i < ViewNodes.Count; i++)
            {
                ViewNode node = ViewNodes[i];
                int depth = node.GetDepth();
                if (depth >= maxWidthPerDepth.Length) { throw new Exception("depth error"); }
                maxWidthPerDepth[depth] = Math.Max(maxWidthPerDepth[depth], (int)node.localBound.size.x);
            }
            
            // 第二步：找到所有根节点（没有父节点的节点）
            List<JsonNode> rootNodes = new List<JsonNode>();
            foreach (var node in Asset.Data.Nodes)
            {
                if (NodeDic.TryGetValue(node, out ViewNode viewNode))
                {
                    // 检查是否为根节点（没有连接到父端口或父端口未连接）
                    if (viewNode.ParentPort == null || !viewNode.ParentPort.connected)
                    {
                        rootNodes.Add(node);
                    }
                }
            }
            
            // 第三步：从根节点开始进行深度优先格式化
            foreach (JsonNode rootNode in rootNodes)
            {
                FormatNode(rootNode, new HashSet<JsonNode>());
            }
            
            // 第四步：强制刷新视图以确保布局更新
            schedule.Execute(() => MarkDirtyRepaint());
        }

        void FormatNode(JsonNode node, HashSet<JsonNode> visitedNodes = null)
        {
            // 防止无限递归
            if (visitedNodes == null) visitedNodes = new HashSet<JsonNode>();
            if (visitedNodes.Contains(node)) return;
            visitedNodes.Add(node);
            
            if (!NodeDic.TryGetValue(node, out ViewNode viewNode)) return;
            
            int maxDepth = viewNode.GetChildMaxDepth();
            int depth = viewNode.GetDepth();
            ViewNode parent = viewNode.GetParent();
            
            // 计算有效的Y位置
            int ValidYPos = parent == null ? 0 : parent.Data.Position.y;
            for (int i = depth; i <= maxDepth; i++)
            {
                ValidYPos = Math.Max(ValidYPos, validYPosPerDepth[i]);
            }
            
            // 设置节点位置
            Rect rect = viewNode.localBound;
            Vec2 newPosition = new Vec2 { x = GetXPos(depth), y = ValidYPos };
            rect.position = newPosition;
            
            // 更新ViewNode位置
            viewNode.SetPosition(rect);
            
            // 更新有效Y位置
            validYPosPerDepth[depth] = ValidYPos + (int)viewNode.localBound.size.y + Y_SPACE;
            
            // 收集并排序子节点
            List<JsonNode> childs = new List<JsonNode>();
            if (viewNode.ChildPorts != null)
            {
                // 按照worldBound.y排序ChildPort以确保正确的顺序
                List<ChildPort> childPorts = viewNode.ChildPorts
                    .Where(port => port != null)
                    .OrderBy(port => port.worldBound.y)
                    .ToList();
                
                foreach (ChildPort port in childPorts)
                {
                    try
                    {
                        List<JsonNode> portChildren = port.GetChildValues();
                        if (portChildren != null)
                        {
                            childs.AddRange(portChildren);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"获取子节点值时出错: {ex.Message}");
                    }
                }
            }
            
            // 递归格式化子节点
            foreach (JsonNode child in childs)
            {
                if (child != null)
                {
                    FormatNode(child, visitedNodes);
                }
            }
        }
        private void FormatAllNodes(DropdownMenuAction a)
        {
            // 开始批量操作记录
            var batchCommand = Window.History.BeginBatch();

            // 记录所有节点的位置变化
            var positionCommands = new List<PropertyChangeCommand>();
            foreach (var viewNode in ViewNodes)
            {
                var oldPosition = viewNode.Data.Position;
                var posCommand = new PropertyChangeCommand(
                    viewNode.Data,
                    "Position",
                    oldPosition,
                    oldPosition // 这里先用旧值，FormatNodes后会被实际的新值替换
                );
                positionCommands.Add(posCommand);
                batchCommand.AddCommand(posCommand);
            }

            // 调用实际的格式化方法
            FormatNodes();

            // 更新批量命令中的新位置值
            for (int i = 0; i < Math.Min(positionCommands.Count, ViewNodes.Count); i++)
            {
                // 通过反射更新newValue
                positionCommands[i].GetType()
                    .GetField("newValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.SetValue(positionCommands[i], ViewNodes[i].Data.Position);
            }

            // 结束批量操作记录
            Window.History.EndBatch(batchCommand);
        }
    }
}
