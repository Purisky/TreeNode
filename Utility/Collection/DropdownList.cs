using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

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
