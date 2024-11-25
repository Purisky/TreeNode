using System.Collections.Generic;
using System.Xml;
using System;
using TreeNode.Utility;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using System.Linq;
using TreeNode.Runtime;

namespace TreeNode.Editor
{
    public partial class TreeNodeGraphView
    {
        const int X_SPACE = 30;
        const int Y_SPACE = 30;
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

        private void FormatNodes()
        {
            if (ViewNodes.Count <= 1) return;
            maxWidthPerDepth = new int[256];
            validYPosPerDepth = new int[256];
            for (int i = 0; i < ViewNodes.Count; i++)
            {
                ViewNode node = ViewNodes[i];
                int depth = node.GetDepth();
                if (depth >= maxWidthPerDepth.Length) { throw new Exception("depth error"); }
                maxWidthPerDepth[depth] = Math.Max(maxWidthPerDepth[depth], (int)node.localBound.size.x);
            }
            for (int i = 0; i < Asset.Data.Nodes.Count; i++)
            {
                JsonNode node = Asset.Data.Nodes[i];
                FormatNode(node);
            }

        }

        void FormatNode(JsonNode node)
        {
            
            ViewNode viewNode = NodeDic[node];
            int maxDepth = viewNode.GetChildMaxDepth();
            int depth = viewNode.GetDepth();
            ViewNode parent = viewNode.GetParent();
            int ValidYPos = parent == null ? 0 : parent.Data.Position.y;
            for (int i = depth; i <= maxDepth; i++)
            {
                ValidYPos = Math.Max(ValidYPos, validYPosPerDepth[i]);
            }
            Rect rect = viewNode.localBound;
            rect.position = new Vec2 { x = GetXPos(depth), y = ValidYPos }; ;
            viewNode.SetPosition(rect);
            validYPosPerDepth[depth] = ValidYPos + (int)viewNode.localBound.size.y + Y_SPACE;
            List<JsonNode> childs = new();
            List<ChildPort> childPorts = viewNode.ChildPorts.OrderBy(n=>n.worldBound.y).ToList();
            for (int i = 0; i < childPorts.Count; i++)
            {
                ChildPort port = childPorts[i];
                childs.AddRange(port.GetChildValues());
            }
            for (int i = 0; i < childs.Count; i++)
            {
                FormatNode(childs[i]);
            }
        }
    }
}
