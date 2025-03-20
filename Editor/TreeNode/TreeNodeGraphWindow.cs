using System;
using UnityEditor;
using UnityEngine.UIElements;
using TreeNode.Runtime;
using UnityEditor.Build.Content;
using UnityEngine;
using System.IO;
using TreeNode.Utility;
using UnityEditor.UIElements;
using System.Reflection;
using UnityEditor.Experimental.GraphView;
namespace TreeNode.Editor
{
    [Serializable]
    public abstract class TreeNodeGraphWindow : EditorWindow , IHasCustomMenu
    {
        public JsonAsset JsonAsset;
        public string Path;
        public string Title;

        public TreeNodeGraphView GraphView { get; private set; }
        protected VisualElement rootView;


        public History History;

        public virtual TreeNodeGraphView CreateTreeNodeGraphView() => new (this);


        public virtual void Init(TreeNodeAsset asset, string path)
        {
            JsonAsset = new JsonAsset() { Data = asset };
            Path = path;
            InitView();

        }
        public void InitView()
        {
            if (rootView == null && Path != null)
            {
                GraphView?.RemoveFromHierarchy();
                Title = System.IO.Path.GetFileNameWithoutExtension(Path);
                if (JsonAsset == null)
                {
                    JsonAsset jsonAsset = JsonAsset.GetJsonAsset(Path);
                    if (jsonAsset == null)
                    {
                        rootVisualElement.Add(new Label($"Json parse error : {Path}"));
                        Debug.LogError($"Json parse error : {Path}");
                        return;
                    }
                    JsonAsset = jsonAsset;
                }
                History = new(this);
                titleContent = new(Title, JsonAssetHelper.GetIcon(JsonAsset.Data.GetType().Name), Path);

                rootView = rootVisualElement;
                rootView.name = "RootView";
                GraphView = CreateTreeNodeGraphView();

                rootView.Insert(0,GraphView);
                rootView.RegisterCallback<KeyDownEvent>(OnKeyDown);
                rootView.RegisterCallback<KeyUpEvent>(OnKeyUp);
            }



        }
        public virtual void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new("刷新界面"), false, Refresh);
        }
        public void Refresh()
        {
            JsonAsset = null;
            rootView = null;
            InitView();
            //Debug.Log("Refresh");
            hasUnsavedChanges = false;
            History.Clear();
        }


        public virtual void OnKeyDown(KeyDownEvent evt)
        {
            if (GraphView == null) { return; }
            bool dirty = false;
            if (evt.ctrlKey)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.S:
                        SaveChanges();
                        break;
                    case KeyCode.Z:
                        if (evt.shiftKey)
                        {
                            dirty = History.Redo();
                        }
                        else
                        {
                            dirty = History.Undo();
                        }
                        break;
                }
            }
            if (dirty) { MakeDirty(); }
        }
        public virtual void OnKeyUp(KeyUpEvent evt)
        {
        }

        public void OnEnable()
        {
            InitView();
        }

        public void MakeDirty()
        {
            //Debug.Log("MakeDirty");
            hasUnsavedChanges = true;
        }
        public override void SaveChanges()
        {
            GraphView?.OnSave();
            GraphView?.SaveAsset();
            Repaint();
            hasUnsavedChanges = false;
            
        }

        public static void CreateFile<T>() where T : TreeNodeAsset
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            JsonAsset jsonAsset = new()
            {
                Data = Activator.CreateInstance<T>()
            };
            string ext = jsonAsset.Data.GetType().Name == "NodePrefabAsset" ? ".pja" : ".ja";
            string name = $"new_{jsonAsset.Data.GetType().Name}";
            string filename = $"{path}/{name}{ext}";
            int index = 0;
            while (File.Exists(filename)) {
                index++;
                filename = $"{path}/{name}({index}){ext}";
            }
            File.WriteAllText(filename, Json.ToJson(jsonAsset));
            AssetDatabase.Refresh();
        }
        public static bool CreateFile<T>(string path,string id) where T : TreeNodeAsset
        {
            JsonAsset jsonAsset = new()
            {
                Data = Activator.CreateInstance<T>()
            };
            string ext = jsonAsset.Data.GetType().Name == "NodePrefabAsset" ? ".pja" : ".ja";
            string filename = $"{path}/{id}{ext}";
            if (File.Exists(filename))
            {
                return false;
            }
            File.WriteAllText(filename, Json.ToJson(jsonAsset));
            AssetDatabase.Refresh();
            return true;
        }

    }

    public static class WindowManager
    {
        public static TWindow Open<TWindow,T>(T target, string path) where T : TreeNodeAsset where TWindow : TreeNodeGraphWindow
        {
            TWindow[] windows = Resources.FindObjectsOfTypeAll<TWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].Path == path)
                {
                    windows[i].Show();
                    windows[i].Focus();
                    return windows[i];
                }
            }
            TWindow window = EditorWindow.CreateWindow<TWindow>(typeof(TWindow),typeof(SceneView));
            window.Init(target,path);
            window.Show();
            return window;
        }
    }

}
