using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine.UIElements;

namespace TreeNode.Editor
{
    public class NumValueDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(NumValue);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PropertyPath path, Action action)
        {
            NumPort port = NumPort.Create(memberMeta, node);
            port.dataSourcePath = path;
            port.SetOnChange(path, action);
            port.InitNumValue(path);
            return new PropertyElement(memberMeta, node, path, this, port);
        }
    }
}
