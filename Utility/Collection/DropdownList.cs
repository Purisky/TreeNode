using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.GenericMenu;

namespace TreeNode.Utility
{
    public class DropdownList<T>:List<DropdownItem<T>>
    {


    }

    public class TreeNodeMenu : GenericDropdownMenu
    {
        public class MenuItem
        {
            public string name;

            public VisualElement element;

            public Action action;

            public Action<object> actionUserData;
        }
    }
}
