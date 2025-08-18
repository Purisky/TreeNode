using System;
namespace TreeNode.Exceptions
{
    public static class TemplateAssetException
    {
        public class FileNotExist : Exception
        {
            public FileNotExist(string id) : base($"TemplateAsset file not exist: {id}") { }
        }
        public class FileIDRepeat : Exception
        {
            public FileIDRepeat(string id, string[] strings) : base($"TemplateAsset file id repeat: {id} \n {string.Join('\n', strings)}") { }
        }
        public class JsonEmpty : Exception
        {
            public JsonEmpty(string path) : base($"TemplateAsset is empty: {path}") { }
        }
        public class DataTypeError : Exception
        {
            public DataTypeError(string path) : base($"Data is not TemplateAsset: {path}") { }
        }
        public class NodeEmpty : Exception
        {
            public NodeEmpty(string path) : base($"No node in TemplateAsset: {path}") { }
        }
    }
}
