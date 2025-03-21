using System.Collections.Generic;
using System.Reflection;
using System;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;
using System.Runtime.InteropServices;
using TreeNode.Utility;

namespace TreeNode.Editor
{
    public class BaseCompositeDrawer<T,Tv, TField, TFieldValue> : BaseDrawer where T : BaseCompositeField<Tv, TField, TFieldValue>,new() where TField : TextValueField<TFieldValue>, new ()
    {
        public override Type DrawType => typeof(Tv);
        //public override PropertyElement Create(MemberInfo memberInfo, ViewNode node, PropertyPath path, Action action)
        //{
        //    ShowInNodeAttribute showInNode = memberInfo.GetCustomAttribute<ShowInNodeAttribute>();
        //    LabelInfoAttribute labelInfo = memberInfo.GetLabelInfo();
        //    T field = new()
        //    {
        //        name = labelInfo.Text,
        //        dataSourcePath = path
        //    };
        //    field.style.flexGrow = 1;
        //    field.style.height = 20;
        //    field.Insert(0, CreateLabel(labelInfo));

        //    Tv value = node.Data.GetValue<Tv>(in path);
        //    field.SetValueWithoutNotify(value);
        //    object parent = node.Data.GetParent(path);
        //    action = memberInfo.GetOnChangeAction(parent) + action;
        //    bool dirty = memberInfo.SerializeByJsonDotNet();
        //    field.RegisterValueChangedCallback(evt =>
        //    {
        //        node.Data.SetValue(in path, evt.newValue);
        //        if (dirty)
        //        {
        //            field.SetDirty();
        //        }
        //        action?.Invoke();
        //    });
        //    field.SetEnabled(!showInNode.ReadOnly);
        //    return new PropertyElement(memberInfo, node, path, this, field);
        //}
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, string path, Action action)
        {
            ShowInNodeAttribute showInNode = memberMeta.ShowInNode;
            LabelInfoAttribute labelInfo = memberMeta.LabelInfo;
            T field = new()
            {
                name = labelInfo.Text,
                dataSourcePath = new(path)
            };
            field.style.flexGrow = 1;
            field.style.height = 20;
            field.Insert(0, CreateLabel(labelInfo));

            Tv value = node.Data.GetValue<Tv>(path);
            field.SetValueWithoutNotify(value);
            object parent = node.Data.GetParent(path);
            action = memberMeta.OnChangeMethod.GetOnChangeAction(parent) + action;
            bool dirty = memberMeta.Json;
            field.RegisterValueChangedCallback(evt =>
            {
                node.Data.SetValue( path, evt.newValue);
                if (dirty)
                {
                    field.SetDirty();
                }
                action?.Invoke();
            });
            field.SetEnabled(!showInNode.ReadOnly);
            return new PropertyElement(memberMeta, node, path, this, field);
        }
    }




    public class Vector2Drawer : BaseCompositeDrawer<Vector2Field, Vector2, FloatField, float> { }
    public class Vector3Drawer : BaseCompositeDrawer<Vector3Field, Vector3, FloatField, float> { }
    public class Vector2IntDrawer : BaseCompositeDrawer<Vector2IntField, Vector2Int, IntegerField, int> { }
    public class Vector3IntDrawer : BaseCompositeDrawer<Vector3IntField, Vector3Int, IntegerField, int> { }
    

}
