# Mina-Sprite-Repacker
This is a C# console app for extracting and repacking Mina the Hollower sprites that were unpacked using the experimental modding branch of Mina the Hollower on Steam.

## Instructions
1) Set your game to the experimental-modding branch in Steam under `Properties->Game Versions & Betas`
2) Set the launch arguments in Steam to `-mod -unpak` under `Properties->General`
3) Launch the game and navigate to `%appdata%\Yacht Club Games\Mina the Hollower` and verify that there is a `mods` folder there now, with a folder inside called `unpak` containing all the unpacked files.
4) Remove the `-unpak` launch argument from Steam to make sure it does not unpack the files every time you launch the game!
5) [Download the latest release](https://github.com/phil-macrocheira/Mina-Sprite-Repacker/releases) and place `mina-sr.exe` in `unpak\data`
6) Run `mina-sr -e` to extract all sprites to a new folder called `_my_sprites`
7) I recommend moving the `unpak` folder elsewhere. You may also want to keep an unmodified copy so that you don't have to unpack the files again. If you do so, create a new mod in the `mods` folder by making a folder named whatever you want, with a `data` folder inside and a copy of the `mod.yc` from `unpak`. You can edit the `mod.yc` file in any text editor to change information about the mod. Lastly, move or copy `mina-sr.exe` to this new mod.
8) If you made a new mod, the structure is the same as `unpak`, but only include the .anb.yc sprites and corresponding .pal.yc palettes that you are modding. Also include the `_my_sprites` folder, which should also include only sprites you are modding.
9) Modify sprites
10) Run `mina-sr -r` to repack all sprites in the `_my_sprites` folder into the files in the `data` folder. You can also run `mina-sr -r "filepath"` to repack a specific sprite.
11) Run the game and the sprite you modified should have changed.