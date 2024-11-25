using Newtonsoft.Json;
using System;
using Unity.Properties;
using UnityEngine;

namespace TreeNode.Utility
{
    public class PathConverter : JsonConverter<PropertyPath>
    {
        public override PropertyPath ReadJson(JsonReader reader, Type objectType, PropertyPath existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            return new PropertyPath(reader.Value as string);
        }

        public override void WriteJson(JsonWriter writer, PropertyPath value, JsonSerializer serializer)
        {
            writer.WriteValue(value.ToString());
        }
    }
}
