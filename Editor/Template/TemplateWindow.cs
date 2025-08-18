using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    [NodeAsset(typeof(TemplateAsset))]
    public class TemplateWindow : TreeNodeGraphWindow
    {
        public new TemplateGraphView GraphView { get; private set; }
        public override TreeNodeGraphView CreateTreeNodeGraphView() => new TemplateGraphView(this);

        [MenuItem("Assets/Create/TreeNode/Template")]
        public static void CreateFile()
        {
            CreateFile<TemplateAsset>();
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
