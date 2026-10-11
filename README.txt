AC8 HOTAS Binder 1.0.1
======================
by PhantomLasagne  (GitHub: PhantomLasagne, Nexus Mods: Ph4ntomLasagne)

A point-and-press binding editor for the AC8HOTAS mod for Ace Combat 8.

Click a game action, press the button or move the axis on your stick, throttle
or pedals, and the binder writes it into AC8HOTAS's bindings.lua for you.
No more copying device GUIDs and button numbers by hand.


REQUIREMENTS
------------
- AC8HOTAS 0.2.0, installed and working (tested with 0.2.0).
- Windows 10 or 11. Uses .NET Framework 4, which is already part of Windows.
- Your controllers plugged in.


INSTALL
-------
1. Extract the AC8-HOTAS-Binder folder into your AC8HOTAS mod folder, so you get:

     ...\UE4SS\Mods\AC8HOTAS\bindings.lua
     ...\UE4SS\Mods\AC8HOTAS\AC8-HOTAS-Binder\AC8-HOTAS-Binder.exe

   (Anywhere else works too. The binder will ask you to pick the AC8HOTAS folder
   the first time and remember it.)

2. Launch Ace Combat 8 once with your controllers plugged in, then quit.
   AC8HOTAS writes the list of controllers the game can see, which the
   binder needs.

The binder doesn't affect UE4SS or the game. To uninstall, delete its folder.


HOW TO USE
----------
1. Close the game, then run AC8-HOTAS-Binder.exe.
2. Click an action (or select it and press Enter). The bar at the bottom turns
   amber and the binder listens:
     - Buttons: press the button or hat direction you want.
     - Axes: do what the prompt says, e.g. "push the stick FORWARD". The way
       you move it sets Normal or Inverted automatically.
   Esc cancels.
3. Repeat for everything you want. Click "Clear" (or press Delete) to unbind.
   Click an axis's Direction cell to flip Normal/Inverted.
4. Click "Save bindings" (Ctrl+S). The button shows how many unsaved changes
   you have, and the status line turns amber until you save.
5. Launch the game and test. If something feels off, quit, adjust it in the
   binder, save and launch again.

Changing bindings with the game running
  AC8HOTAS reloads bindings.lua when F5 is pressed in game, so you can try
  saving and pressing F5 instead of restarting. It isn't guaranteed to pick up
  every change. If it doesn't seem to work, restart the game, which always loads
  the saved file.

Tips
- The "KB / Mouse" and "Controller" columns show the game's own default
  controls (Expert mode) for reference, so you can see what each action does
  on a keyboard or pad before giving it a HOTAS control. Hover a cell for the
  in-game name. "-" means the game's controls menu doesn't list it.
- One control can drive several actions, e.g. the same hat for Look and the
  menu D-pad. The "Also used by" column shows any sharing.
- Buttons that are already held down when you start listening are ignored, so
  toggle switches resting "on" won't hijack a binding.
- The axis directions follow the usual convention: stick forward = nose down,
  right = roll/yaw right, throttle forward = more thrust. If something feels
  backwards in game, click its Direction cell, save and restart the game.
- Drag the grips between the column headers to resize columns.
- Optional and untested actions (ExtraButton_01 etc.) are under the collapsed
  "Optional & untested" group at the bottom.


WHAT IT CHANGES
---------------
- bindings.lua: only the lines for actions you changed. Your comments, other
  lines and the generated device list are kept exactly as they were.
- bindings.lua.binder.bak: a copy of bindings.lua from just before each save.
  (AC8HOTAS keeps its own bindings.lua.bak, which the game overwrites itself.)
- %APPDATA%\AC8-HOTAS-Binder\folder.txt: remembers your AC8HOTAS folder.

The binder only reads your controllers through the standard Windows HID API.
It never changes controller settings, takes exclusive control of a device,
or sends input to the game.


LIMITS (from AC8HOTAS itself)
-----------------------------
- The game has to have listed a controller before you can bind it. If a device
  shows "(not in game list yet)", launch the game once with it plugged in,
  quit, then click "Rescan devices".
- Two connected controllers of the exact same model can't be told apart.
- Only the first hat (POV) on each device is supported; buttons 1-128.
- Camera actions need real axes. A mini-stick set to digital/button mode in
  its vendor software can be bound to the Look (Hat) actions instead.


TROUBLESHOOTING
---------------
"Windows protected your PC" (SmartScreen)
  The exe isn't code-signed. Click "More info" > "Run anyway". The full source
  is included in the src folder and can be built with build.cmd.

A row says "Edited by hand"
  That line in bindings.lua isn't a simple single-line entry, so the binder
  leaves it alone. Edit it in a text editor, or put it back on one line.

Pressing a control does nothing while listening
  Check the device strip at the top. Green = ready. Amber = not in the game's
  list yet (see LIMITS). If a device is missing entirely, replug it and click
  "Rescan devices".

The game ignored or rejected my bindings
  Restart the game so it loads the saved file. If it still doesn't work, check
  the newest HOTAS-xxxxx.log in the AC8HOTAS folder for the reason. Your
  previous file is in bindings.lua.binder.bak.


BUILDING FROM SOURCE
--------------------
Run build.cmd. It uses the C# compiler included with Windows (.NET Framework
4.x); no SDK or Visual Studio needed. Source is in src\Binder.cs.

Self-test (reads only; never writes your bindings):
  AC8-HOTAS-Binder.exe --selftest "<AC8HOTAS folder>" "<report.txt>"


CREDITS & THANKS
----------------
This tool only exists because of the mods it's built on:

- AC8 HOTAS Support (AC8HOTAS) by Drumsmasher17
    https://www.nexusmods.com/acecombat8wingsoftheve/mods/8
    https://github.com/Drumsmasher17/AC8HOTAS
  The mod that actually gets sticks, throttles and pedals into Ace Combat 8.
  The binder just edits its bindings.lua and reads the device list it writes.

- AC8AnalogYaw by Drumsmasher17 (bundled with AC8HOTAS)
    https://github.com/Drumsmasher17/AC8AnalogueYaw
  Proportional rudder/yaw, which is what makes binding your pedals worth it.

- UE4SS by the UE4SS-RE team
    https://github.com/UE4SS-RE/RE-UE4SS
  The loader both mods run on.

Huge thanks to Drumsmasher17 for the mods and for releasing them under CC0.


LICENSE
-------
MIT License. Copyright (c) 2026 PhantomLasagne. See LICENSE.txt.

Fully open source: use it, modify it, fork it, bundle it or re-upload it, as long
as you keep the copyright notice and LICENSE.txt and credit PhantomLasagne
(GitHub) / Ph4ntomLasagne (Nexus Mods) as the original author.
This tool was developed with AI assistance and is provided without warranty.
This is an unofficial companion tool, not made or endorsed by the authors above.
Bug reports and suggestions: the AC8 HOTAS Binder page on Nexus Mods (Ph4ntomLasagne),
not the AC8HOTAS page.
