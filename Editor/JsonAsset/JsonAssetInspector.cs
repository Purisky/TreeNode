using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace TreeNode.Editor
{


    [CustomEditor(typeof(TextAsset), true)]
    public class JsonAssetInspector : UnityEditor.Editor
    {
        public bool IsJsonAsset;

        public VisualElement Inspector;

        private void OnEnable()
        {
            string path = AssetDatabase.GetAssetPath(target);
            IsJsonAsset = path.EndsWith(".ja") || path.EndsWith(".tpl");
        }


        public void RemoveVE()
        {
            if (Inspector != null)
            {
                Inspector.parent.parent[0].style.display = DisplayStyle.None;
                Inspector.parent.parent.parent[0].style.display = DisplayStyle.None;
            }
        }
        protected override void OnHeaderGUI()
        {
            if (IsJsonAsset)
            {
                RemoveVE();
                return;
            }

            base.OnHeaderGUI();
        }



        public override VisualElement CreateInspectorGUI()
        {
            if (IsJsonAsset)
            {
                Inspector = new VisualElement();
                return Inspector;
            }
            return base.CreateInspectorGUI();
        }
    }
}