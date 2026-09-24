// A fake game for testing the API probe itself: shaped like tests/probe/GameApi.cs expects, but with
// three deliberate gaps, so the report can be checked for naming exactly those. Missing: the Brain
// type (so Brain.PartyLeader cannot be checked), Cell.IsExplored() and GameObject.IsLedBy().

using System.Collections.Generic;

namespace XRL
{
    public class GamePlayer { public XRL.Messages.MessageQueue Messages; }
    public class XRLGame { public GamePlayer Player; }
    public static class The { public static XRLGame Game; }
}

namespace XRL.Messages
{
    public class MessageQueue { public List<string> Messages; }
}

namespace XRL.World
{
    public class Zone
    {
        public int Width, Height;
        public Cell GetCell(int x, int y) { return null; }
    }

    public class Cell
    {
        public int X, Y;
        public List<GameObject> Objects;
        public bool IsVisible() { return true; }
    }

    public class GameObject
    {
        public Zone CurrentZone;
        public Cell CurrentCell;
        public object GetPart(string name) { return null; }
        public bool HasPart(string name) { return false; }
        public bool HasTag(string name) { return false; }
        public bool IsTakeable() { return false; }
        public int DistanceTo(GameObject o) { return 0; }
        public bool IsPlayerLed() { return false; }
        public bool IsHostileTowards(GameObject o) { return false; }
    }
}

namespace XRL.World.Parts
{
    public class Render { public string ColorString; public int RenderLayer; public bool Visible; }
}

namespace XRL.World.Capabilities
{
    public static class Scanning { public static bool HasScanningFor(XRL.World.GameObject a, XRL.World.GameObject b) { return false; } }
}

namespace XRL.Rules
{
    public static class Strings { public static string WoundLevel(XRL.World.GameObject o) { return ""; } }
}
