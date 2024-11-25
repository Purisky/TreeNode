using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
{
    [Serializable, PortColor("#ffffff")]
    public class JsonNode
    {
        [JsonProperty]
        public Vec2 Position;



        [JsonProperty]
        public PrefabData PrefabData;


        //public string PrefabId;
        //[JsonProperty]
        //public Dictionary<string, object> Dic;


        public virtual T GetValue<T>(in PropertyPath path)
        {

            if (PropertyContainer.TryGetValue(this, in path, out T Value))
            {
                return Value;
            }
            return default;
        }
                
            
            
        public virtual void SetValue<T>(in PropertyPath path, T value)
        {






            IProperty property = PropertyContainer.GetProperty(this, in path);
            if (property.IsReadOnly)
            {
                Debug.Log(property.DeclaredValueType().Name);
            }
            PropertyContainer.SetValue(this, in path, value);
        }

        JsonNode GetEndNode(in PropertyPath path)
        {
            if (path.Length == 0)
            {
                return this;
            }



            PropertyPath propertyPath = PropertyPath.Pop(path);
            return GetValue<JsonNode>(in propertyPath);
        }


        public virtual void SetValueInternal<T>(in PropertyPath path, T value)
        {
            PropertyContainer.SetValue(this, in path, value);
        }




        public object GetParent(in PropertyPath path)
        {
            if (path.Length <=1)
            {
                return this;
            }
            PropertyPath propertyPath = PropertyPath.Pop(path);
            return GetValue<object>(in propertyPath);

        }
    }




    public struct Vec2
    {
        public int x;
        public int y;

        public static implicit operator Vector2(Vec2 vec2)
        {
            return new Vector2(vec2.x, vec2.y);
        }
        public static implicit operator Vec2(Vector2 vec2)
        {
            return new Vec2 { x = (int)vec2.x, y = (int)vec2.y };
        }
        public Vec2(int x_, int y_) { x = x_;y = y_; }
        public Vec2(float x_,float y_) { x = (int)x_; y = (int)y_; }
        public Vec2(Length x_, Length y_) { x = (int)x_.value; y = (int)y_.value; }
        public Vec2(StyleLength x_, StyleLength y_) { x = (int)x_.value.value; y = (int)y_.value.value; }
    }

    public abstract class PrefabData {
        public abstract string ID { get; }
        public abstract string Name { get; }
    }


}