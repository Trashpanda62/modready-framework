ModReady — Bring Your Own (BYO) Music
=====================================

Drop your own audio files into the folders below and ModReady plays them in
that game situation instead of the vanilla track — without overwriting a
single vanilla file. Remove the files (or the mod) and vanilla music returns.

HOW IT WORKS
------------
The folder tree IS the settings. A folder with no audio files = vanilla plays
for that situation (nothing happens). Drop files in, restart the game, they
play. ModReady regenerates its PSAI soundtrack from this tree every launch.

FILE FORMAT
-----------
  * Campaign / battle / siege / menu / naval contexts : .ogg ONLY.
      (The game's audio layer forces the .ogg extension on these; a .wav here
       is ignored. Convert to .ogg first — e.g. with Audacity or ffmpeg.)
  * Settlement contexts (Town / Village / Tavern)      : .ogg or .wav.

FOLDERS
-------
  Menu\            Main menu.
  CampaignPeace\   Roaming the campaign map in peacetime.
  CampaignWar\     Campaign map, tension / at war / dramatic.
  CampaignDark\    Campaign map, low morale / dark.
  BattleSmall\     Small field battles.
  BattleMedium\    Medium field battles.
  BattleLarge\     Large field battles.
  Siege\           Sieges.
  Victory\         Battle won.
  Defeat\          Battle lost.
  Naval\           Naval battles + sea sailing (War Sails DLC only).
  Settlement\Town\     Walking around a town.
  Settlement\Village\  Walking around a village.
  Settlement\Tavern\   Inside a tavern.

NOTES
-----
  * Multiple files in a folder = they rotate (shuffle by default).
  * You'll see a "PC" subfolder appear next to your files after first launch —
    that's ModReady staging a link the game's audio loader needs. Leave it.
  * Check Modules\ModReady\runtime.log for lines tagged PsaiRedirect /
    SoundtrackXmlGen to confirm your tracks were picked up.
