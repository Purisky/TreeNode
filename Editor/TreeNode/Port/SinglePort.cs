using System;
using System.Collections.Generic;
using TreeNode.Runtime;

namespace TreeNode.Editor
{
    public class SinglePort : ChildPort
    {
        protected SinglePort(Type type) : base(Capacity.Single, type)
        {
        }
        public static SinglePort Create(MemberMeta meta)
        {
            Type type = meta.Type;
            SinglePort port = new(type)
            {
                Meta = meta,
                tooltip = type.Name,
                portName = meta.LabelInfo.Hide?null: meta.LabelInfo.Text,
            };
            return port;
        }
        public override List<JsonNode> GetChildValues()
        {
            object portValue = GetPortValue();
            if (portValue == null) { return new(); }
            return new() { portValue as JsonNode };
        }

        public override void SetNodeValue(JsonNode child, bool remove = true)
        {
            node.Data.SetValue(Meta.Path, remove ? null : child);
        }
    }
}
