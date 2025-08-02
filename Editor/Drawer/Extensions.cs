using System.Collections.Generic;
using TreeNode.Runtime;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public static class Extensions
    {
        public static void SetDirty(this VisualElement visualElement)
        {
            ViewNode viewNode = visualElement.GetFirstAncestorOfType<ViewNode>();
            viewNode?.View.Window.MakeDirty();
        }
        public static void RecordField<T>(this VisualElement visualElement,PAPath path,T oldValue,T newValue)
        {
            visualElement.GetFirstAncestorOfType<ViewNode>()?.RecordField<T>(path,oldValue,newValue);
        }
        public static void RecordField<T>(this ViewNode viewNode, PAPath path, T oldValue, T newValue)
        {
            if (viewNode == null) { return; }
            viewNode.View.Window.History.Record(new FieldModifyOperation<T>(viewNode.Data, path, oldValue, newValue, viewNode.View));
            viewNode.View.Window.MakeDirty();
        }
        public static void RecordItem(this ListView listView, PAPath path, object Value,int from,int to )
        {
            ViewNode viewNode = listView.GetFirstAncestorOfType<ViewNode>();
            if (viewNode == null) { return; }
            viewNode.View.Window.History.Record(new ListItemModifyOperation(viewNode.Data, path, from, to, Value, viewNode.View));
            viewNode.View.Window.MakeDirty();
        }

    }
}
