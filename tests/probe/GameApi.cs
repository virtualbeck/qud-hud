// Every game member the mod reaches only by reflection, one per line. `python build.py --check`
// compiles this against the installed game: a line that fails to compile names a guess the mod has
// wrong, which in the game would only ever show up as a panel quietly saying "unavailable".
//
// Each line mirrors how the mod uses the member (a method is called, a field or property is read),
// and is labelled with a "probe:" comment that build.py reports by. Lines marked "known good" are
// members already seen working in game; if one of those fails, the probe itself is broken. This file
// is never shipped with the mod.

// The types are declared inside the method on purpose. An error in a declaration, such as a missing
// type in a parameter list, makes the compiler stop before it checks any method body, which would
// leave every guess below silently unchecked. Inside the body, each failure is reported on its own.
static class GameApiProbe
{
    static void Probe()
    {
        object v;
        XRL.World.Zone z = null;                            // probe: type Zone
        XRL.World.Cell c = null;                            // probe: type Cell
        XRL.World.GameObject o = null;                      // probe: type GameObject
        XRL.World.Parts.Render r = null;                    // probe: type Render
        XRL.World.Parts.Brain b = null;                     // probe: type Brain

        // the map and the nearby objects list
        v = z.Width;                                        // probe: Zone.Width
        v = z.Height;                                       // probe: Zone.Height
        v = z.GetCell(0, 0);                                // probe: Zone.GetCell(x, y)
        v = c.X;                                            // probe: Cell.X (known good)
        v = c.IsVisible();                                  // probe: Cell.IsVisible() (known good)
        v = c.IsExplored();                                 // probe: Cell.IsExplored()
        v = c.Objects;                                      // probe: Cell.Objects
        v = o.CurrentZone;                                  // probe: GameObject.CurrentZone
        v = o.CurrentCell;                                  // probe: GameObject.CurrentCell (known good)
        v = o.GetPart("Render");                            // probe: GameObject.GetPart(string) (known good)
        v = o.HasPart("StairsDown");                        // probe: GameObject.HasPart(string) (known good)
        v = o.HasTag("Plant");                              // probe: GameObject.HasTag(string)
        v = o.IsTakeable();                                 // probe: GameObject.IsTakeable()
        v = o.DistanceTo(o);                                // probe: GameObject.DistanceTo(GameObject) (known good)
        v = r.ColorString;                                  // probe: Render.ColorString
        v = r.RenderLayer;                                  // probe: Render.RenderLayer
        v = r.Visible;                                      // probe: Render.Visible

        // who counts as a companion: the mod tries these in turn
        v = o.IsPlayerLed();                                // probe: GameObject.IsPlayerLed()
        v = b.PartyLeader;                                  // probe: Brain.PartyLeader
        v = o.IsLedBy(o);                                   // probe: GameObject.IsLedBy(GameObject)
        v = o.IsHostileTowards(o);                          // probe: GameObject.IsHostileTowards(GameObject) (known good)

        // the message log: the mod prefers a list called Messages on this queue
        v = XRL.The.Game;                                   // probe: The.Game
        v = XRL.The.Game.Player;                            // probe: The.Game.Player
        v = XRL.The.Game.Player.Messages;                   // probe: The.Game.Player.Messages
        v = XRL.The.Game.Player.Messages.Messages;          // probe: MessageQueue.Messages

        // health as the game shows it
        v = XRL.World.Capabilities.Scanning.HasScanningFor(o, o);  // probe: Scanning.HasScanningFor (known good)
        v = XRL.Rules.Strings.WoundLevel(o);                // probe: Strings.WoundLevel (known good)
    }
}
