using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public static class Extensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.History.AddStep();
        }

    }
}
