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
                
                // 延迟注册值变化监听，避免初始化期间的性能问题
                schedule.Execute(RegisterValueChangeCallbacks).ExecuteLater(100);
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
        /// 注册值变化回调 - 优化版本
        /// </summary>
        private void RegisterValueChangeCallbacks()
        {
            // 使用更高效的查询方式，避免重复查询
            var allFields = new Dictionary<Type, List<VisualElement>>();
            
            // 一次性收集所有相关字段
            CollectUIElements(allFields);
            
            // 批量注册回调
            RegisterCallbacksBatch(allFields);
        }

        /// <summary>
        /// 批量收集UI元素 - 🔥 修复：只收集直接属于当前PropertyElement的UI元素，避免收集嵌套PropertyElement的元素
        /// </summary>
        private void CollectUIElements(Dictionary<Type, List<VisualElement>> allFields)
        {
            // 🔥 修复策略：使用深度优先遍历，但遇到嵌套的PropertyElement时停止向下递归
            CollectUIElementsRecursive(this, allFields, 0);
        }

        /// <summary>
        /// 🔥 新增：递归收集UI元素，但避免跨越PropertyElement边界
        /// </summary>
        private void CollectUIElementsRecursive(VisualElement current, Dictionary<Type, List<VisualElement>> allFields, int depth)
        {
            // 防止无限递归
            if (depth > 20) return;

            // 检查当前元素是否为可监听的输入控件
            var elementType = current.GetType();
            if (IsMonitorableElement(elementType))
            {
                if (!allFields.ContainsKey(elementType))
                {
                    allFields[elementType] = new List<VisualElement>();
                }
                allFields[elementType].Add(current);
            }

            // 🔥 关键修复：遍历子元素，但跳过嵌套的PropertyElement
            for (int i = 0; i < current.childCount; i++)
            {
                var child = current[i];
                
                // 如果子元素是PropertyElement且不是当前PropertyElement，则跳过其子树
                // 这样避免了收集嵌套PropertyElement中的控件
                if (child is PropertyElement childPropertyElement && childPropertyElement != this)
                {
                    continue; // 跳过嵌套PropertyElement的整个子树
                }

                // 递归处理非PropertyElement的子元素
                CollectUIElementsRecursive(child, allFields, depth + 1);
            }
        }

        /// <summary>
        /// 判断是否为可监听的元素类型
        /// </summary>
        private bool IsMonitorableElement(Type elementType)
        {
            return elementType == typeof(TextField) ||
                   elementType == typeof(FloatField) ||
                   elementType == typeof(IntegerField) ||
                   elementType == typeof(Toggle) ||
                   elementType == typeof(EnumField) ||
                   elementType.IsSubclassOf(typeof(BaseField<>));
        }

        /// <summary>
        /// 批量注册回调
        /// </summary>
        private void RegisterCallbacksBatch(Dictionary<Type, List<VisualElement>> allFields)
        {
            foreach (var kvp in allFields)
            {
                var elementType = kvp.Key;
                var elements = kvp.Value;

                // 根据类型批量注册
                if (elementType == typeof(TextField))
                {
                    foreach (var element in elements.Cast<TextField>())
                    {
                        element.RegisterValueChangedCallback(OnTextFieldValueChanged);
                    }
                }
                else if (elementType == typeof(FloatField))
                {
                    foreach (var element in elements.Cast<FloatField>())
                    {
                        element.RegisterValueChangedCallback(OnFloatFieldValueChanged);
                    }
                }
                else if (elementType == typeof(IntegerField))
                {
                    foreach (var element in elements.Cast<IntegerField>())
                    {
                        element.RegisterValueChangedCallback(OnIntFieldValueChanged);
                    }
                }
                else if (elementType == typeof(Toggle))
                {
                    foreach (var element in elements.Cast<Toggle>())
                    {
                        element.RegisterValueChangedCallback(OnToggleValueChanged);
                    }
                }
                else if (elementType == typeof(EnumField))
                {
                    foreach (var element in elements.Cast<EnumField>())
                    {
                        element.RegisterValueChangedCallback(OnEnumFieldValueChanged);
                    }
                }
            }
        }

        // 优化的值变化处理器 - 增加防抖动
        private DateTime _lastChangeTime = DateTime.MinValue;
        private const int ChangeThrottleMs = 50; // 50毫秒防抖动

        /// <summary>
        /// 处理文本字段值变化
        /// </summary>
        private void OnTextFieldValueChanged(ChangeEvent<string> evt)
        {
            if (ShouldThrottleChange()) return;
            RecordFieldModification<string>(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理浮点数字段值变化
        /// </summary>
        private void OnFloatFieldValueChanged(ChangeEvent<float> evt)
        {
            if (ShouldThrottleChange()) return;
            RecordFieldModification<float>(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理整数字段值变化
        /// </summary>
        private void OnIntFieldValueChanged(ChangeEvent<int> evt)
        {
            if (ShouldThrottleChange()) return;
            RecordFieldModification<int>(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理布尔字段值变化
        /// </summary>
        private void OnToggleValueChanged(ChangeEvent<bool> evt)
        {
            if (ShouldThrottleChange()) return;
            RecordFieldModification<bool>(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 处理枚举字段值变化
        /// </summary>
        private void OnEnumFieldValueChanged(ChangeEvent<Enum> evt)
        {
            if (ShouldThrottleChange()) return;
            RecordFieldModification<Enum>(evt.previousValue, evt.newValue);
        }

        /// <summary>
        /// 判断是否应该限制变化频率
        /// </summary>
        private bool ShouldThrottleChange()
        {
            var now = DateTime.Now;
            if ((now - _lastChangeTime).TotalMilliseconds < ChangeThrottleMs)
            {
                return true;
            }
            _lastChangeTime = now;
            return false;
        }

        /// <summary>
        /// 🔥 新增：泛型版本的字段修改记录方法 - 减少装箱操作
        /// </summary>
        private void RecordFieldModification<T>(T oldValue, T newValue)
        {
            if (!_isMonitoringValue) return;
            
            try
            {
                // 快速值比较，避免不必要的处理
                if (FastValueEquals<T>(oldValue, newValue)) return;
                
                // 🔥 调试信息：记录触发字段修改的PropertyElement详细信息
                Debug.Log($"🔥 字段修改触发: PropertyElement[{GetGlobalPath()}] " +
                         $"LocalPath='{LocalPath}' MemberPath='{MemberMeta.Path}' " +
                         $"值变化: '{oldValue}' -> '{newValue}' (类型: {typeof(T).Name})");
                
                // 🔥 使用泛型版本的FieldModifyOperation，避免装箱
                var fieldModifyOperation = new FieldModifyOperation<T>(
                    ViewNode.Data,
                    MemberMeta.Path,  // 使用MemberMeta.Path而不是GetGlobalPath()
                    oldValue,
                    newValue,
                    ViewNode.View as TreeNodeGraphView
                );
                
                // 记录到历史系统
                if (ViewNode.View is TreeNodeGraphView graphView)
                {
                    graphView.Window.History.RecordOperation(fieldModifyOperation);
                    Debug.Log($"✅ 字段修改已记录到历史系统: Node={ViewNode.Data.GetType().Name}, Field={MemberMeta.Path}, Type={typeof(T).Name}");
                }
                
                _lastValue = newValue;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"记录字段修改失败: {e.Message}");
            }
        }

        /// <summary>
        /// 🔥 新增：泛型版本的快速值比较
        /// </summary>
        private bool FastValueEquals<T>(T oldValue, T newValue)
        {
            if (ReferenceEquals(oldValue, newValue)) return true;
            if (oldValue == null || newValue == null) return false;
            
            // 对于值类型和简单类型，直接比较
            var type = typeof(T);
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum || type.IsValueType)
            {
                return EqualityComparer<T>.Default.Equals(oldValue, newValue);
            }
            
            // 对于复杂类型，使用引用比较
            return ReferenceEquals(oldValue, newValue);
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
