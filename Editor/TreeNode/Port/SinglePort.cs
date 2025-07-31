using System;
using System.Collections.Generic;
using TreeNode.Runtime;

namespace TreeNode.Editor
{
    public class SinglePort : ChildPort
    {
        protected SinglePort(MemberMeta meta,Type type) : base(meta,Capacity.Single, type)
        {
        }
        public static SinglePort Create(MemberMeta meta)
        {
            Type type = meta.Type;
            SinglePort port = new(meta,type)
            {
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

        public override PAPath SetNodeValue(JsonNode child, bool remove = true)
        {
            node.Data.SetValue(Meta.Path, remove ? null : child);
            return Meta.Path;
        }
    }
}
