using System;
using TreeNode.Runtime;
using Unity.Properties;
using UnityEngine;

namespace TreeNode.Editor
{
    public class NumValueDrawer : BaseDrawer
    {
        public override Type DrawType => typeof(NumValue);
        public override PropertyElement Create(MemberMeta memberMeta, ViewNode node, PAPath path, Action action)
        {
            NumPort port = NumPort.Create(memberMeta, node);
            port.dataSourcePath =new( path);
            port.SetOnChange(path, action);
            port.InitNumValue(path);
            return new PropertyElement(memberMeta, node, path, this, port);
        }
    }
}
