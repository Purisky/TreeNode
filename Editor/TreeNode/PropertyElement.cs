using System;
using System.Collections;
using System.Reflection;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;
using UnityEngine;
using TreeNode.Utility;
using UnityEngine.Experimental.GlobalIllumination;

namespace TreeNode.Editor
{
    public class PropertyElement : ShowIfElement
    {
        public ViewNode ViewNode;
        public string LocalPath;
        public MemberMeta MemberMeta;

        public BaseDrawer Drawer;

        public PrefabProperty PrefabProperty;
        public NodePrefabAsset NodePrefabAsset => GraphView.AssetData;
        public NodePrefabGraphView GraphView;
        static readonly StyleSheet StyleSheet = ResourcesUtil.LoadStyleSheet("PropertyElement");
        
        // 字段值监听和历史记录
        private object _lastValue;
        private bool _isMonitoringValue = false;
        
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
            RegisterPrefab();
            
            // 开始监听字段值变化
            StartValueMonitoring();
        }

        /// <summary>
        /// 开始监听字段值变化
        /// </summary>
        private void StartValueMonitoring()
        {
            if (_isMonitoringValue) return;
            
            try
            {
                // 获取当前值作为基线
                _lastValue = GetCurrentFieldValue();
                _isMonitoringValue = true;
                
                // 注册值变化监听
                RegisterValueChangeCallbacks();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"无法启动字段值监听: {e.Message}");
            }
        }

        /// <summary>
        /// 获取当前字段值
        /// </summary>
        private object GetCurrentFieldValue()
        {
            try
            {
                return ViewNode.Data.GetValue<object>(MemberMeta.Path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 注册值变化回调
        /// </summary>
        private void RegisterValueChangeCallbacks()
        {
            // 为不同类型的UI元素注册监听器
            RegisterTextFieldCallbacks();
            RegisterNumericFieldCallbacks();
            RegisterBooleanFieldCallbacks();
            RegisterEnumFieldCallbacks();
        }

        private void RegisterTextFieldCallbacks()
        {
            var textFields = this.Query<TextField>().ToList();
            foreach (var textField in textFields)
            {
                textField.RegisterValueChangedCallback(OnTextFieldValueChanged);
            }
        }

        private void RegisterNumericFieldCallbacks()
        {
            var floatFields = this.Query<FloatField>().ToList();
            foreach (var floatField in floatFields)
            {
                floatField.RegisterValueChangedCallback(OnFloatFieldValueChanged);
            }

            var intFields = this.Query<IntegerField>().ToList();
            foreach (var intField in intFields)
            {
                intField.RegisterValueChangedCallback(OnIntFieldValueChanged);
            }
        }

        private void RegisterBooleanFieldCallbacks()
        {
            var toggles = this.Query<Toggle>().ToList();
            foreach (var toggle in toggles)
            {
                toggle.RegisterValueChangedCallback(OnToggleValueChanged);
            }
        }

        private void RegisterEnumFieldCallbacks()
        {
            var enumFields = this.Query<EnumField>().ToList();
            foreach (var enumField in enumFields)
            {
                enumField.RegisterValueChangedCallback(OnEnumFieldValueChanged);
            }
        }

        /// <summary>
        /// 处理文本字段值变化
        /// </summary>
        private void OnTextFieldValueChanged(ChangeEvent<string> evt)
        {
            RecordFieldModification(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理浮点数字段值变化
        /// </summary>
        private void OnFloatFieldValueChanged(ChangeEvent<float> evt)
        {
            RecordFieldModification(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理整数字段值变化
        /// </summary>
        private void OnIntFieldValueChanged(ChangeEvent<int> evt)
        {
            RecordFieldModification(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理布尔字段值变化
        /// </summary>
        private void OnToggleValueChanged(ChangeEvent<bool> evt)
        {
            RecordFieldModification(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理枚举字段值变化
        /// </summary>
        private void OnEnumFieldValueChanged(ChangeEvent<Enum> evt)
        {
            RecordFieldModification(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 记录字段修改操作
        /// </summary>
        private void RecordFieldModification(object oldValue, object newValue)
        {
            if (!_isMonitoringValue) return;
            
            try
            {
                // 避免记录相同值的变化
                if (object.Equals(oldValue, newValue)) return;
                
                // 序列化值用于历史记录
                string oldValueJson = Json.ToJson(oldValue);
                string newValueJson = Json.ToJson(newValue);
                
                // 创建字段修改操作
                var fieldModifyOperation = new FieldModifyOperation(
                    ViewNode.Data,
                    GetGlobalPath(),
                    oldValueJson,
                    newValueJson,
                    ViewNode.View as TreeNodeGraphView
                );
                
                // 记录到历史系统
                if (ViewNode.View is TreeNodeGraphView graphView)
                {
                    graphView.Window.History.RecordOperation(fieldModifyOperation);
                }
                
                _lastValue = newValue;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"记录字段修改失败: {e.Message}");
            }
        }

        /// <summary>
        /// 停止值监听
        /// </summary>
        private void StopValueMonitoring()
        {
            _isMonitoringValue = false;
        }

        /// <summary>
        /// 清理资源
        /// </summary>
        public void Dispose()
        {
            StopValueMonitoring();
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

        public string GetGlobalPath() => $"{ViewNode.GetNodePath()}.{LocalPath}";

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
        public string Path;
        public Type DeclaringType;
        public Type Type;
        public LabelInfoAttribute LabelInfo;
        public ShowInNodeAttribute ShowInNode;
        public DropdownAttribute Dropdown;
        public bool Json;
        public MethodInfo OnChangeMethod;
        public string DropdownKey;

        public MemberMeta(MemberInfo member, string path)
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
