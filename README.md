![](logo.png)
# KindredInnkeeper for V Rising

KindredInnkeeper creates an inn for V Rising, allowing new players to join and get started quickly even with no plots being available.

Feel free to reach out to me on Discord (odjit) if you have any questions or need help with the mod.

## Details
To get started, you will need a player named "InnKeeper" and they must have a clan called "Inn".
Place a heart, and build the inn as you wish to have it. I recommend rooms having a wood coffin, at least one chest, and a neutral research table.
For the inn to function best, I also recommend grabbing Kindred Schematics to lock/movelock the territory once completed, and to block the relocation of the castle heart.
Once your layout is complete, use the `.inn addroom` command to add rooms to the inn. Once a room is added, players can claim it using the `.inn claimroom` command. Players can leave their room at any time using the `.inn leaveroom` command.
While on the territory, players will be immune to the sun, and will have a slight speed increase from the cursed forest wisp.
Players cannot build anything while in the clan.
Players cannot loot chests in another's claimed room.
Players can only claim one room at a time.
You cannot join the inn if you are in a clan or already have a plot.
Players cannot open the door to another's claimed room.
If a player is in a room, they can leave it, even if it is not their room.
If a player goes to place a castle heart, they will be warned that that will cause them to be removed from the Inn. Once they again place the heart, they will get removed and anything stored in the chests will get brought to their new plot in travel bags.


## Command List

### Staff Commands
- `.inn addroom`
  - Adds a room to the inn.
- `.inn removeroom`
  - Removes a room from the inn.
- `.inn guests`
  - Lists the current guests in the inn.
- `.inn setroomowner (player)`
  - Sets the owner of the room to the specified player.
- `.inn roomowner`
  - Names the owner of the room you are in.

### Player Accessible Commands:
- `.inn enter`
  - Adds the user to the inn clan.
- `.inn rules`
  - Lists the rules of the inn.
- `.inn quests`
  - Completes quests relating to a castle heart.
- `.inn vacancy`
  - Lists the current vacancies in the inn.
- `.inn claimroom`
  - Claims a room in the inn.
- `.inn leaveroom`
  - Removes the user from the room

 

[V Rising Modding Discord](https://vrisingmods.com/discord)                     |          [V Rising Modding Wiki](https://wiki.vrisingmods.com)



## Installation
<details> <summary>Steps</summary>

1. Install BepInEx, which is required for modding VRising. Follow the instructions provided at [BepInEx Installation Guide](https://wiki.vrisingmods.com/user/bepinex_install.html) to set it up correctly in your VRising game directory.

2. Download the KindredInnkeeper mod along with its dependencies (VCF). Ensure you select the correct versions that are compatible with your game.

3. After downloading, locate the .dll files for KindredInnkeeper and its dependencies. Move or copy these .dll files into the `BepInEx\Plugins` directory within your VRising installation folder.

   - **Single Player Note:**
     - If you are playing in single player mode, you will need to install [ServerLaunchFix](https://thunderstore.io/c/v-rising/p/Mythic/ServerLaunchFix/). This is a server-side mod that is essential for making the commands work properly on the client side. Make sure to download and place it in the same `BepInEx\Plugins` directory.

4. Launch the Game: Start VRising. If everything has been set up correctly, KindredInnkeeper should now be active in the game.

</details>
<details><summary>Additional Notes</summary>

- **Using Commands:** The commands for KindredInnkeeper go into the chat box, not the console. However, players will first need to authenticate themselves in the console chat. You can find instructions on how to do this [here](https://wiki.vrisingmods.com/user/Using_Server_Mods.html).
- For thorough mod installation instructions and troubleshooting, visit [VRising Mod Installation Guide](https://wiki.vrisingmods.com/user/Mod_Install.html).
- If you encounter any issues, refer to the V Rising Modding Community discord for tech support. 
</details>




## Credits

- [V Rising Modding Community](https://vrisingmods.com) for support and ideas.

## License

This project is licensed under the AGPL-3.0 license.