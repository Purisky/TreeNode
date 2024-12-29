using System;
using System.Collections;
using System.Reflection;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;
using UnityEngine;
using TreeNode.Utility;

namespace TreeNode.Editor
{
    public class PropertyElement : ShowIfElement
    {
        public ViewNode ViewNode;
        public PropertyPath LocalPath;
        public MemberMeta MemberMeta;

        public BaseDrawer Drawer;

        public PrefabProperty PrefabProperty;
        public NodePrefabAsset NodePrefabAsset => GraphView.AssetData;
        public NodePrefabGraphView GraphView;
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("PropertyElement");
        public PropertyElement(MemberMeta memberMeta, ViewNode viewNode, PropertyPath path, BaseDrawer drawer, VisualElement visualElement = null)
        {
            MemberMeta = memberMeta;
            ViewNode = viewNode;
            LocalPath = path;
            Drawer = drawer;
            if (visualElement != null)
            {
                Add(visualElement);
            }
            styleSheets.Add(StyleSheet);
            if (MemberMeta.ShowInNode!=null&&!string.IsNullOrEmpty(MemberMeta.ShowInNode.ShowIf))
            {
                object parent = viewNode.Data.GetParent(MemberMeta.Path);
                Type type = parent.GetType();

                MemberInfo memberInfo = type.GetMember(MemberMeta.ShowInNode.ShowIf, BindingFlags.NonPublic| BindingFlags.Public| BindingFlags.Static| BindingFlags.Instance)[0];
                if (memberInfo != null)
                {
                    switch (memberInfo.MemberType)
                    {
                        case MemberTypes.Field:
                            ShowIf = () => (bool)((FieldInfo)memberInfo).GetValue(parent);
                            break;
                        case MemberTypes.Method:
                            ShowIf = ((MethodInfo)memberInfo).CreateDelegate(typeof(Func<bool>), parent) as Func<bool>;
                            break;
                        case MemberTypes.Property:
                            ShowIf = ((PropertyInfo)memberInfo).GetMethod.CreateDelegate(typeof(Func<bool>), parent) as Func<bool>;
                            break;
                    }
                }
                if (ShowIf != null)
                {
                    viewNode.ShowIfElements.Add(this);
                }
            }
            RegisterPrefab();
        }



        public void RegisterPrefab()
        {
            if (ViewNode.View is not NodePrefabGraphView) { return; }
            GraphView = ViewNode.View as NodePrefabGraphView;
            RegisterCallback<MouseOverEvent>(OnMouseEnter);
            RegisterCallback<MouseOutEvent>(OnMouseOut);
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            InitPrefabProperty();
        }
        public void InitPrefabProperty()
        {
            //Debug.Log(MemberMeta);
            PrefabProperty = new()
            {
                Path = GetGlobalPath(),
                Name = MemberMeta.LabelInfo.Text,
                Type = MemberMeta.Type.FullName
            };
            if (MemberMeta.Type.Inherited(typeof(IList)) && Drawer is not ListDrawer)
            {
                PrefabProperty.Type = MemberMeta.Type.GenericTypeArguments[0].FullName;
            }

            //Debug.Log(PrefabProperty.Path);
            for (int i = 0; i < NodePrefabAsset.Properties.Count; i++)
            {
                //Debug.Log($"{NodePrefabAsset.Properties[i].Path}=>{PrefabProperty.Path}");
                if (NodePrefabAsset.Properties[i].Path == PrefabProperty.Path)
                {
                    PrefabProperty = NodePrefabAsset.Properties[i];
                    AddToClassList("PrefabPropertySelected");
                    Output = true;
                    NodePrefabInfoProperty nodePrefabInfoProperty = GraphView.NodePrefabInfo.FindProperty(PrefabProperty);
                    nodePrefabInfoProperty.ViewNode = ViewNode;
                    nodePrefabInfoProperty.PropertyElement = this;
                    break;
                }
            }
        }



        public bool Selected;
        public bool Output;

        public void SetSelection(bool selected)
        {
            if (Selected == selected) { return; }
            Selected = selected;
            if (Selected)
            {
                AddToClassList("PrefabPropertyHover");
                if (Output)
                {
                    GraphView.NodePrefabInfo.FindProperty(PrefabProperty)?.AddToClassList("Link2Node");
                }
            }
            else
            {
                RemoveFromClassList("PrefabPropertyHover");
                if (Output)
                {
                    GraphView.NodePrefabInfo.FindProperty(PrefabProperty)?.RemoveFromClassList("Link2Node");
                }
            }
        }






        public bool SetOutput(bool output)
        {
            Output = output;
            if (Output)
            {
                AddToClassList("PrefabPropertySelected");
                PrefabProperty.Path = GetGlobalPath();
                if (!NodePrefabAsset.Properties.Contains(PrefabProperty))
                {
                    NodePrefabAsset.Properties.Add(PrefabProperty);
                    GraphView.NodePrefabInfo.Add(this);
                    return true;
                }

            }
            else
            {
                RemoveFromClassList("PrefabPropertySelected");
                if (NodePrefabAsset.Properties.Remove(PrefabProperty))
                {
                    GraphView.NodePrefabInfo.Remove(PrefabProperty);
                    return true;
                }
            }
            return false;
        }
        public bool Valid()
        {
            VisualElement child = ElementAt(0);
            if (child is ChildPort port)
            {
                return !port.connected;
            }
            if (child is NumPort numPort)
            {
                return !numPort.connected;
            }
            return true;
        }



        public PropertyPath GetGlobalPath() => PropertyPath.Combine(ViewNode.GetNodePath(), LocalPath);

        void OnMouseEnter(MouseOverEvent evt)
        {
            if (!Valid())
            {
                evt.StopPropagation();
                return;
            }
            NodePrefabWindow.CurrentHover?.SetSelection(false);
            NodePrefabWindow.CurrentHover = this;
            evt.StopPropagation();

            if (evt.ctrlKey)
            {
                SetSelection(true);
            }
            else
            {
                SetSelection(false);
            }
        }
        void OnMouseOut(MouseOutEvent evt)
        {
            if (!Valid()) { return; }
            if (NodePrefabWindow.CurrentHover == this)
            {
                SetSelection(false);
                NodePrefabWindow.CurrentHover = null;
            }
        }
        void OnMouseDown(MouseDownEvent evt)
        {
            if (!Valid()) { return; }
            if (!Selected || !evt.ctrlKey) { return; }
            if (SetOutput(!Output))
            {
                GraphView.Window.History.AddStep();
            }
        }

    }


    public struct MemberMeta
    {
        public PropertyPath Path;
        public Type DeclaringType;
        public Type Type;
        public LabelInfoAttribute LabelInfo;
        public ShowInNodeAttribute ShowInNode;
        public DropdownAttribute Dropdown;
        public bool Json;
        public MethodInfo OnChangeMethod;
        public string DropdownKey;

        public MemberMeta(MemberInfo member, PropertyPath path)
        {
            Type = member.GetValueType();
            DeclaringType = member.DeclaringType;
            ShowInNode = member.GetCustomAttribute<ShowInNodeAttribute>();
            LabelInfo = member.GetLabelInfo();
            Dropdown = member.GetCustomAttribute<DropdownAttribute>();
            if (Dropdown == null&& Type.IsSubclassOf(typeof(Enum)))
            {
                Dropdown = new(null);
            }
            Json = member.SerializeByJsonDotNet();
            OnChangeMethod = member.GetMethodInfo();
            Path = path;
            DropdownKey = null;
            if (Type.IsSubclassOf(typeof(Enum)))
            {
                DropdownKey = Type.Name;
            }
            else
            {
                if (Dropdown != null)
                {
                    DropdownKey = $"{member.DeclaringType.Name}.{member.Name}";
                }
            }
            
        }

    }

}
