using Newtonsoft.Json;
using System;
using UnityEngine;

namespace TreeNode.Utility
{
    public static class Json
    {
        static Json()
        {
            jsonSettings = new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = Formatting.Indented,
                TypeNameHandling = TypeNameHandling.Auto,
                Error = (sender, args) =>
                {
                    args.ErrorContext.Handled = true;
                }
            };
            jsonSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
        }
        static readonly JsonSerializerSettings jsonSettings;
        public static void Log(object obj)
        {
            Debug.Log(ToJson(obj));
        }

        public static string ToJson(object obj)
        {
            return JsonConvert.SerializeObject(obj, jsonSettings);
        }
        public static T Get<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, jsonSettings);
        }
        public static object Get(Type type,string json)
        {
            return JsonConvert.DeserializeObject(json,type, jsonSettings);
        }
        public static T DeepCopy<T>(T obj)
        {
            return Get<T>(ToJson(obj));
        }
        public static object DeepCopy(object obj)
        {
            return Get(obj.GetType(),ToJson(obj));
        }
        public static string ToJson(Enum obj) => obj.ToString();

        public static string ToJson(string obj) => obj;


    }
}

