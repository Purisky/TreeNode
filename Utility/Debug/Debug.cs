using System;
using System.Drawing;
using System.Numerics;
namespace TreeNode.Utility
{
    public class Debug : Singleton<Debug>
    {
        Action<string> LogAction;
        Action<string> Error;
        Action<Vector2, Vector2,Color, float> DrawLineAction;
        public static void Log(string message)
        {
            Inst.LogAction?.Invoke(message);
        }
        public static void Log(object message)
        {
            Inst.LogAction?.Invoke(message.ToString());
        }
        public static void LogError(string message)
        {
            Inst.Error?.Invoke(message);
        }
        public static void DrawLine(Vector2 start, Vector2 end,Color color,float time)
        {
            Inst.DrawLineAction?.Invoke(start,end, color, time);
        }




        public static void Init(Action<string> log, Action<string> error, Action<Vector2, Vector2, Color, float> action )
        {
            Inst.LogAction = log;
            Inst.Error = error;
            Inst.DrawLineAction = action;
            Timer.LogAction = log;
        }

    }
}
