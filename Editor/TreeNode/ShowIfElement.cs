using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class ShowIfElement : VisualElement
    {
        public Func<bool> ShowIf;
        public void Refresh()
        {
            if (ShowIf != null)
            {
                style.display = ShowIf() ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }
    }
}
