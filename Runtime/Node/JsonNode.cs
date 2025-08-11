using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Reflection;
using TreeNode.Utility;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace TreeNode.Runtime
{
    [Serializable, PortColor("#ffffff")]
    public partial class JsonNode
    {

        [JsonProperty]
        public Vec2 Position;
        [JsonProperty]
        public PrefabData PrefabData;
        public virtual T GetValue<T>(string path) => PropertyAccessor.GetValue<T>(this, path);
        public virtual void SetValue<T>(string path, T value) => PropertyAccessor.SetValue<T>(this, path, value);
        public bool SetValue(Type type, string key, JToken value)
        {
            if (type == null || string.IsNullOrEmpty(key))
            {
                return false;
            }
            MemberInfo memberInfo = type.GetMember(key)[0];
            if (memberInfo == null)
            {
                return false;
            }
            try
            {
                object obj = value.ToObject(memberInfo.GetValueType());
                memberInfo.SetValue(this, obj);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error setting value: {e.Message}");
                return false;
            }
            return true;
        }
        public object GetParent(string path)
        {
            string parentPath = PropertyAccessor.ExtractParentPath(path);
            if (parentPath == null)
            {
                return this;
            }
            return GetValue<object>(parentPath);
        }
        public virtual string GetInfo() => GetType().Name;



        //public virtual T GetValueInternal<T>(ref PAPath path, ref int index) => throw new NotImplementedException();
        //public virtual void SetValueInternal<T>(ref PAPath path, ref int index, T value)=> throw new NotImplementedException();
        //public virtual void RemoveValueInternal(ref PAPath path, ref int index)=> throw new NotImplementedException();
    }




    [GenIPropertyAccessor]
    public partial struct Vec2: IEquatable<Vec2>
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
        public override readonly string ToString()
        {
            return $"({x}, {y})";
        }

        public readonly bool Equals(Vec2 other)
        {
            return x == other.x && y == other.y;
        }
        public override readonly bool Equals(object obj)
        {
            return obj is Vec2 other && Equals(other);
        }
        public override readonly int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
        public static bool operator ==(Vec2 left, Vec2 right)
        {
            return left.Equals(right);
        }
        public static bool operator !=(Vec2 left, Vec2 right)
        {
            return !left.Equals(right);
        }
        public static Vec2 operator +(Vec2 a, Vec2 b)
        {
            return new Vec2(a.x + b.x, a.y + b.y);
        }
        public static Vec2 operator -(Vec2 a, Vec2 b)
        {
            return new Vec2(a.x - b.x, a.y - b.y);
        }


    }

    public abstract class PrefabData {
        public abstract string ID { get; }
        public abstract string Name { get; }
    }


}
