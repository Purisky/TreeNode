using System;
namespace TreeNode.Exceptions
{
    public static class NodePrefabAssetException
    {
        public class FileNotExist : Exception
        {
            public FileNotExist(string id) : base($"NodePrefabAsset file not exist: {id}") { }
        }
        public class FileIDRepeat : Exception
        {
            public FileIDRepeat(string id, string[] strings) : base($"NodePrefabAsset file id repeat: {id} \n {string.Join('\n', strings)}") { }
        }
        public class JsonEmpty : Exception
        {
            public JsonEmpty(string path) : base($"NodePrefabAsset is empty: {path}") { }
        }
        public class DataTypeError : Exception
        {
            public DataTypeError(string path) : base($"Data is not NodePrefabAsset: {path}") { }
        }
        public class NodeEmpty : Exception
        {
            public NodeEmpty(string path) : base($"No node in NodePrefabAsset: {path}") { }
        }
    }
}
