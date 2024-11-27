using TreeNode.Runtime;
using UnityEditor;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    [NodeAsset(typeof(NodePrefabAsset))]
    public class NodePrefabWindow : TreeNodeGraphWindow
    {
        public new NodePrefabGraphView GraphView { get; private set; }
        public override TreeNodeGraphView CreateTreeNodeGraphView() => new NodePrefabGraphView(this);

        [MenuItem("Assets/Create/TreeNode/NodePrefab")]
        public static void CreateFile()
        {
            CreateFile<NodePrefabAsset>();
        }

        public override void Init(TreeNodeAsset asset, string path)
        {
            base.Init(asset, path);
        }



        public override void OnKeyDown(KeyDownEvent evt)
        {
            base.OnKeyDown(evt);
            if (evt.ctrlKey)
            {
                CurrentHover?.SetSelection(true);
            }
        }
        public override void OnKeyUp(KeyUpEvent evt)
        {
            base.OnKeyUp(evt);
            CurrentHover?.SetSelection(false);
        }



        public static PropertyElement CurrentHover;

    }
}
