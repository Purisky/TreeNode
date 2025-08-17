using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TreeNode.Runtime;
using TreeNode.Utility;
using UnityEditor;
using UnityEditor.VersionControl;
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
    public abstract class TreeNodeGraphWindow : EditorWindow , IHasCustomMenu, ISerializationCallbackReceiver
    {
        [SerializeField]
        public JsonAsset JsonAsset;
        [SerializeField]
        public string Path;
        [SerializeField]
        public string Title;

        public TreeNodeGraphView GraphView { get; private set; }
        protected VisualElement rootView;


        public History History;

        // Serialization callback fields
        [SerializeField]
        protected bool _isDeserializing = false;

        public virtual TreeNodeGraphView CreateTreeNodeGraphView() => new (this);

        // ISerializationCallbackReceiver implementation
        public virtual void OnBeforeSerialize()
        {
            //Debug.Log($"TreeNodeGraphWindow.OnBeforeSerialize() - Path: {Path}, Title: {Title}, WindowState: {(IsWindowStateValid() ? "Valid" : "Invalid")}");
            
            // 在序列化前验证状态，如果状态无效则清理
            if (!string.IsNullOrEmpty(Path) && !File.Exists(Path))
            {
                Debug.LogWarning($"TreeNodeGraphWindow.OnBeforeSerialize() - File missing, clearing path: {Path}");
                Path = null;
                JsonAsset = null;
                Title = null;
            }
        }

        public virtual void OnAfterDeserialize()
        {
            //Debug.Log($"TreeNodeGraphWindow.OnAfterDeserialize() - Path: {Path}, Title: {Title}");
            _isDeserializing = true;
            
            // 如果反序列化后发现路径为空，标记为无效状态
            if (string.IsNullOrEmpty(Path))
            {
                Debug.LogWarning("TreeNodeGraphWindow.OnAfterDeserialize() - No path found after deserialization");
                _isDeserializing = false; // 不需要延迟处理
            }
        }

        // Public method to reset deserialization flag
        public void SetNotDeserializing()
        {
            _isDeserializing = false;
        }


        public virtual void Init(TreeNodeAsset asset, string path)
        {
            //Debug.Log($"TreeNodeGraphWindow.Init() called - Path: {path}, Asset: {asset?.GetType().Name}");
            JsonAsset = new JsonAsset() { Data = asset };
            Path = path;
            _isDeserializing = false; // 明确标记为用户主动打开
            InitView();
        }
        
        public void InitView()
        {
            //Debug.Log($"TreeNodeGraphWindow.InitView() - Path: {Path}, File exists: {(!string.IsNullOrEmpty(Path) ? File.Exists(Path) : "Path is null/empty")}");
            
            if (rootView == null && Path != null)
            {
                // 改进的文件存在性检查
                if (!string.IsNullOrEmpty(Path))
                {
                    bool fileExists = File.Exists(Path);
                    //Debug.Log($"TreeNodeGraphWindow.InitView() - File check: {Path}, Exists: {fileExists}, IsDeserializing: {_isDeserializing}");
                    
                    if (!fileExists)
                    {
                        if (_isDeserializing)
                        {
                            // 对于序列化恢复的情况，给AssetDatabase更多时间刷新
                            //Debug.Log($"TreeNodeGraphWindow.InitView() - Delaying file check for deserialized window: {Path}");
                            EditorApplication.delayCall += () =>
                            {
                                if (this != null && !File.Exists(Path))
                                {
                                    Debug.LogWarning($"File not found after delay (deserialization): {Path}. Closing window.");
                                    Close();
                                }
                                else if (this != null)
                                {
                                    Debug.Log($"File found after delay (deserialization): {Path}. Continuing initialization.");
                                    // 重新调用InitView以完成初始化
                                    _isDeserializing = false;
                                    InitView();
                                }
                            };
                            return;
                        }
                        else
                        {
                            Debug.LogWarning($"File not found (immediate): {Path}. Closing window.");
                            Close();
                            return;
                        }
                    }
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
                History = new(()=>this.MakeDirty());
                titleContent = new(Title, JsonAssetHelper.GetIcon(JsonAsset.Data.GetType().Name), Path);

                rootView = rootVisualElement;
                rootView.name = "RootView";
                GraphView = CreateTreeNodeGraphView();

                rootView.Insert(0,GraphView);
                rootView.RegisterCallback<KeyDownEvent>(OnKeyDown);
                rootView.RegisterCallback<KeyUpEvent>(OnKeyUp);
                
                //Debug.Log($"TreeNodeGraphWindow.InitView() completed successfully for: {Path}");
            }
            else
            {
                //Debug.Log($"TreeNodeGraphWindow.InitView() skipped - RootView exists: {rootView != null}, Path: {Path}");
            }
        }
        
        public virtual void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new(I18n.Editor.Menu.ForceReloadView), false, Refresh);
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
                        evt.StopPropagation();
                        break;
                    case KeyCode.Z:
                        List<ViewChange> changes;
                        if (evt.shiftKey)
                        {
                            changes = History.Redo();
                            dirty = changes.Any();
                        }
                        else
                        {
                            changes = History.Undo();
                            dirty = changes.Any();
                        }
                        GraphView.ApplyChanges(changes);
                        evt.StopPropagation();
                        break;
                    case KeyCode.X:
                        evt.StopPropagation();
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
            //Debug.Log($"TreeNodeGraphWindow.OnEnable() - Path: {Path}, Title: {Title}, HasRootView: {rootView != null}, IsDeserializing: {_isDeserializing}");
            
            // 如果是反序列化但没有有效路径，直接关闭窗口
            if (_isDeserializing && string.IsNullOrEmpty(Path))
            {
                Debug.LogWarning("TreeNodeGraphWindow.OnEnable() - Deserialized window has no path, closing");
                Close();
                return;
            }
            
            if (_isDeserializing)
            {
                // 延迟初始化以确保文件系统和AssetDatabase准备就绪
                EditorApplication.delayCall += () =>
                {
                    if (this != null) // 确保窗口仍然存在
                    {
                        _isDeserializing = false;
                        //Debug.Log($"TreeNodeGraphWindow.DelayedInit() - Path: {Path}");
                        InitView();
                    }
                };
            }
            else
            {
                InitView();
            }
        }

        public void OnDisable()
        {
            // Unregister this window when it's closed
            TreeNodeAssetPostprocessor.UnregisterWindow(this);
            //Debug.Log($"TreeNodeGraphWindow.OnDisable() - Path: {Path}, Reason: Window closing");
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
        public void RemoveChangeMark()
        {
            hasUnsavedChanges = false;
        }
        // Helper method to check if the associated file exists
        public bool FileExists()
        {
            if (string.IsNullOrEmpty(Path))
                return false;
                
            return File.Exists(Path);
        }

        // Helper method to validate window state
        public bool IsWindowStateValid()
        {
            bool pathValid = !string.IsNullOrEmpty(Path);
            bool fileExists = pathValid && File.Exists(Path);
            bool jsonAssetValid = JsonAsset != null && JsonAsset.Data != null;
            
            //Debug.Log($"TreeNodeGraphWindow.IsWindowStateValid() - Path: {pathValid}, File: {fileExists}, JsonAsset: {jsonAssetValid}");
            return pathValid && fileExists && jsonAssetValid;
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
                //Debug.Log($"WindowManager.Open() - Checking existing window: {windows[i].Path} vs {path}");
                if (windows[i].Path == path)
                {
                    //Debug.Log($"WindowManager.Open() - Found existing window for: {path}");
                    windows[i].Show();
                    windows[i].Focus();
                    return windows[i];
                }
            }
            
            //Debug.Log($"WindowManager.Open() - Creating new window for: {path}");
            TWindow window = EditorWindow.CreateWindow<TWindow>(typeof(TWindow),typeof(SceneView));
            window.SetNotDeserializing(); // 明确标记这不是反序列化
            window.Init(target,path);
            window.Show();
            return window;
        }
    }
}
