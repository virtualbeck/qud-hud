// Stand-ins for the handful of game types the mod names directly, so the mod can be compiled here
// without the game installed. Everything else it touches goes through reflection and needs nothing.
// The shapes match how the mod uses them, which the game already accepts; they are not the game's
// real definitions, so a clean compile here proves our own code is sound, not that the game's API
// is still what the mod expects. `python build.py --check` compiles against the real game instead.

namespace XRL
{
    using System;
    using XRL.World;

    // partial so a test harness can add what it needs for the mod to find by reflection
    public static partial class The
    {
        public static GameObject Player { get; set; }
    }

    public interface IPlayerMutator
    {
        void mutate(GameObject player);
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class PlayerMutator : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class HasCallAfterGameLoaded : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class CallAfterGameLoaded : Attribute { }
}

namespace XRL.World
{
    public class GameObject
    {
        public bool HasPart(string name) { return false; }
        public IPart AddPart(IPart part) { return part; }
    }

    public class EndTurnEvent { public static readonly int ID = 1; }
    public class BeginTakeActionEvent { public static readonly int ID = 2; }

    public abstract class IPart
    {
        public GameObject ParentObject { get; set; }
        public virtual bool WantEvent(int ID, int cascade) { return false; }
        public virtual bool HandleEvent(EndTurnEvent E) { return true; }
        public virtual bool HandleEvent(BeginTakeActionEvent E) { return true; }
    }
}

namespace UnityEngine
{
    public static class Debug
    {
        // kept so a harness can check what the mod would have written to Player.log
        public static readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
        public static void Log(object message) { Lines.Add(message + ""); }
        public static void LogWarning(object message) { Lines.Add(message + ""); }
    }
}
