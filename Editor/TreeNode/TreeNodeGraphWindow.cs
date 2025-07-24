using System;
using System.IO;
using System.Collections.Generic;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
namespace TreeNode.Editor
{
    /// <summary>
    /// Asset post processor to monitor file deletions and close associated windows
    /// </summary>
    public class TreeNodeAssetPostprocessor : AssetPostprocessor
    {
        // List of all open TreeNodeGraphWindow instances
        private static readonly List<TreeNodeGraphWindow> _openWindows = new List<TreeNodeGraphWindow>();

        // Register a window to be monitored for file deletions
        public static void RegisterWindow(TreeNodeGraphWindow window)
        {
            if (window != null && !_openWindows.Contains(window))
            {
                _openWindows.Add(window);
            }
        }

        // Unregister a window
        public static void UnregisterWindow(TreeNodeGraphWindow window)
        {
            if (window != null)
            {
                _openWindows.Remove(window);
            }
        }

        // Called when assets are deleted, imported, or moved
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            // Check for deleted assets
            if (deletedAssets != null && deletedAssets.Length > 0)
            {
                // Create a list to store windows to close
                var windowsToClose = new List<TreeNodeGraphWindow>();

                foreach (var window in _openWindows)
                {
                    if (window != null && !string.IsNullOrEmpty(window.Path))
                    {
                        // Check if this window's path matches any deleted asset
                        foreach (var deletedAsset in deletedAssets)
                        {
                            if (window.Path.Equals(deletedAsset, StringComparison.OrdinalIgnoreCase))
                            {
                                windowsToClose.Add(window);
                                break;
                            }
                        }
                    }
                }

                // Close windows with deleted files
                foreach (var window in windowsToClose)
                {
                    //Debug.Log($"Closing window for deleted asset: {window.Path}");
                    window.Close();
                    UnregisterWindow(window);
                }
            }
        }
    }

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
                // First check if the file exists - if not, close this window
                if (!File.Exists(Path) && !string.IsNullOrEmpty(Path))
                {
                    Debug.LogWarning($"File not found: {Path}. Closing window.");
                    Close();
                    return;
                }

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
            // Register this window for file deletion monitoring
            TreeNodeAssetPostprocessor.RegisterWindow(this);
            InitView();
        }

        public void OnDisable()
        {
            // Unregister this window when it's closed
            TreeNodeAssetPostprocessor.UnregisterWindow(this);
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

        // Helper method to check if the associated file exists
        public bool FileExists()
        {
            if (string.IsNullOrEmpty(Path))
                return false;
                
            return File.Exists(Path);
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
            if (!path.StartsWith("Assets/"))
            { 
                int index = path.IndexOf("Assets/");
                path = path[index..];
            }
            
            // Check if the file exists before opening
            if (!File.Exists(path))
            {
                Debug.LogWarning($"Cannot open window for non-existent file: {path}");
                return null;
            }
            
            TWindow[] windows = Resources.FindObjectsOfTypeAll<TWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                //Debug.Log($"{windows[i].Path}=>{path}");
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
