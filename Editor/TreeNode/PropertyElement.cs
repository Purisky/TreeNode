using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;
using UnityEngine;
using TreeNode.Utility;
using UnityEngine.Experimental.GlobalIllumination;
using Debug = TreeNode.Utility.Debug;

namespace TreeNode.Editor
{
    public class PropertyElement : ShowIfElement
    {
        public ViewNode ViewNode;
        public PAPath LocalPath;
        public MemberMeta MemberMeta;

        public BaseDrawer Drawer;

        public TemplateProperty TemplateProperty;
        public TemplateAsset TemplateAsset => GraphView.AssetData;
        public TemplateGraphView GraphView;
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("PropertyElement");
        
        public PropertyElement(MemberMeta memberMeta, ViewNode viewNode, string path, BaseDrawer drawer, VisualElement visualElement = null)
        {
            MemberMeta = memberMeta;
            ViewNode = viewNode;
            name = path?? $"_{memberMeta.Type.Name}";
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
            this.AddManipulator(new ContextualMenuManipulator(BuildContextualMenu));
            RegisterTemplate();
           
        }

        public void RegisterTemplate()
        {
            if (ViewNode.View is not TemplateGraphView) { return; }
            GraphView = ViewNode.View as TemplateGraphView;
            RegisterCallback<MouseOverEvent>(OnMouseEnter);
            RegisterCallback<MouseOutEvent>(OnMouseOut);
            RegisterCallback<MouseDownEvent>(OnMouseDown);
            InitTemplateProperty();
        }
        public void InitTemplateProperty()
        {
            //Debug.Log(MemberMeta);
            TemplateProperty = new()
            {
                Path = GetGlobalPath(),
                Name = MemberMeta.LabelInfo.Text,
                Type = MemberMeta.Type.FullName
            };
            if (MemberMeta.Type.Inherited(typeof(IList)) && Drawer is not ListDrawer)
            {
                TemplateProperty.Type = MemberMeta.Type.GenericTypeArguments[0].FullName;
            }

            for (int i = 0; i < TemplateAsset.Properties.Count; i++)
            {
                if (TemplateAsset.Properties[i].Path == TemplateProperty.Path)
                {
                    TemplateProperty = TemplateAsset.Properties[i];
                    AddToClassList("TemplatePropertySelected");
                    Output = true;
                    TemplateInfoProperty templateInfoProperty = GraphView.TemplateInfo.FindProperty(TemplateProperty);
                    templateInfoProperty.ViewNode = ViewNode;
                    templateInfoProperty.PropertyElement = this;
                    break;
                }
            }
        }
        public virtual void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
        }

        public bool Selected;
        public bool Output;

        public void SetSelection(bool selected)
        {
            if (Selected == selected) { return; }
            Selected = selected;
            if (Selected)
            {
                AddToClassList("TemplatePropertyHover");
                if (Output)
                {
                    GraphView.TemplateInfo.FindProperty(TemplateProperty)?.AddToClassList("Link2Node");
                }
            }
            else
            {
                RemoveFromClassList("TemplatePropertyHover");
                if (Output)
                {
                    GraphView.TemplateInfo.FindProperty(TemplateProperty)?.RemoveFromClassList("Link2Node");
                }
            }
        }

        public bool SetOutput(bool output)
        {
            Output = output;
            if (Output)
            {
                AddToClassList("TemplatePropertySelected");
                TemplateProperty.Path = GetGlobalPath();
                if (!TemplateAsset.Properties.Contains(TemplateProperty))
                {
                    TemplateAsset.Properties.Add(TemplateProperty);
                    GraphView.TemplateInfo.Add(this);
                    return true;
                }

            }
            else
            {
                RemoveFromClassList("TemplatePropertySelected");
                if (TemplateAsset.Properties.Remove(TemplateProperty))
                {
                    GraphView.TemplateInfo.Remove(TemplateProperty);
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

        public string GetGlobalPath() => $"{ViewNode.GetNodePath()}.{LocalPath}";

        void OnMouseEnter(MouseOverEvent evt)
        {
            if (!Valid())
            {
                evt.StopPropagation();
                return;
            }
            TemplateWindow.CurrentHover?.SetSelection(false);
            TemplateWindow.CurrentHover = this;
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
            if (TemplateWindow.CurrentHover == this)
            {
                SetSelection(false);
                TemplateWindow.CurrentHover = null;
            }
        }
        void OnMouseDown(MouseDownEvent evt)
        {
            if (!Valid()) { return; }
            if (!Selected || !evt.ctrlKey) { return; }
            if (SetOutput(!Output))
            {
                Debug.Log($"PropertyElement输出状态变化: {TemplateProperty.Path} -> {Output}");
            }
        }
    }


    public struct MemberMeta
    {
        public PAPath Path;
        public Type DeclaringType;
        public Type Type;
        public LabelInfoAttribute LabelInfo;
        public ShowInNodeAttribute ShowInNode;
        public DropdownAttribute Dropdown;
        public StyleAttribute Style;
        public bool Json;
        public MethodInfo OnChangeMethod;
        public string DropdownKey;

        public MemberMeta(MemberInfo member, PAPath path)
        {
            Type = member.GetValueType();
            DeclaringType = member.DeclaringType;
            ShowInNode = member.GetCustomAttribute<ShowInNodeAttribute>();
            LabelInfo = member.GetLabelInfo();
            Dropdown = member.GetCustomAttribute<DropdownAttribute>();
            Style = member.GetCustomAttribute<StyleAttribute>();
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
