![Logo](https://i.imgur.com/GCIbhpn.png)

# ONI Together: An Oxygen Not Included Multiplayer Mod (WIP)

> **Note:** This is a work-in-progress project. Not to be confused with [onimp/oni_multiplayer](https://github.com/onimp/oni_multiplayer).

A new mod that introduces multiplayer functionality to _Oxygen Not Included_, featuring a custom networking layer and lobby system.

> **Note:** This mod is in the very early pre-pre alpha stage.

Join the [Discord](https://discord.gg/jpxveK6mmY)

Steam workshop: [You can find its workshop page here](https://steamcommunity.com/sharedfiles/filedetails/?id=3630759126)

---

## Demo

![Multiplayer Demo](https://i.ibb.co/G136FH2/download.jpg)

---

## Whats done and in progress?

A public trello board exists that tracks whats been done, and what is coming in the next update you can find that [here](https://trello.com/b/kq7yVWyU/oxygen-not-included-together)

## Found an issue?

Raise it on the issues page, and make a bug report on the discord.

Please at least try to include a video if you can. It makes replicating it so much easier

---

## Why not just contribute to the old multiplayer mod?

I like the old multiplayer mod — I do — and kudos to the guys that made it. But its implementation is very limited without a lot of extra effort, if not a full rewrite.  
On top of this, it hasn't seen activity in over 6 months.

> **NOTE:** as of June 6th 2025, when I started this project.

Initially it was just conceptual, but once I got lobbies and packets set up, I knew I was onto something.

## Setup

To get started with building the mod, follow these steps:

1. **Install .NET Framework 4.7.2**  
   Make sure you have [.NET Framework 4.7.2](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net472) installed on your system.

2. **Clone the repository**

   ```
   git clone https://github.com/Lyraedan/Oxygen_Not_Included_Together.git
   ```

3. **Open the solution**  
   Open the `.sln` file in Visual Studio (or your preferred C# IDE).

4. **Copy the file `Directory.Build.props.default` and rename it to `Directory.Build.props.user` file, then adjust the paths**  
   Open the `.csproj` file and find the file under `Solution Items`.
   Copy it and rename the copy to `Directory.Build.props.user`, then open that new file.
   Change its variable `GameLibsFolder` to point to your local `OxygenNotIncluded_Data/Managed` folder, for example:
   ```xml
   <GameLibsFolder>C:\Program Files (x86)\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed</GameLibsFolder>
   ```
   then adjust the variable `ModFolder` to point at your local dev folder, for example:
   ```xml
   <ModFolder>E:\Documents\Klei\OxygenNotIncluded\mods\dev</ModFolder>
   ```
5. **(optional) Restore NuGet packages**
   
7. **Run dotnet tool restore in packet manager console** (Or any other viable console)

8. **IF YOU'RE ON LINUX YOU WILL NEED DOTNET 6.0 TO RUN THE PUBLICISER SEE INSTRUCTIONS [*HERE*](https://github.com/Lyraedan/Oxygen_Not_Included_Together/wiki/Setting-up-publiciser-requirement-on-Linux)**

9. **Build the project**  
   Once the `ManagedPath` is correctly set, build the project.  
   If everything is configured correctly, the build should succeed.
   If there are missing reference errors, restart Visual Studio, the solution creates a publicized reference library the first time a build runs and this can confuse the IDE.

---

## Debugging the Game

To attach a managed debugger (e.g. Visual Studio) directly to a running ONI instance:

### 1. Find the Unity version

Check your player log at `%APPDATA%\..\LocalLow\Klei\Oxygen Not Included\Player.log` — the first few lines will show the Unity version (e.g. `Unity 2022.3.62f2`).

### 2. Grab the development player binaries

Download that exact Unity version from [unity.com/releases](https://unity.com/releases/editor/archive) and navigate to:

```
Editor\Data\PlaybackEngines\windowsstandalonesupport\Variations\win64_development_mono\
```

Copy these three files to ONI's installation folder, renaming `WindowsPlayer.exe` → `OxygenNotIncluded.exe`, replacing the originals:

- `WindowsPlayer.exe` → `OxygenNotIncluded.exe`
- `UnityPlayer.dll`
- `WinPixEventRuntime.dll`

> **Backup the originals first!** Otherwise you'll need to verify game files via Steam to restore them.

### 3. Enable the debugger wait flag

Open `OxygenNotIncluded_Data\boot.config` in a text editor and append:

```
wait-for-managed-debugger=1
player-connection-debug=1
```

### 4. Attach Visual Studio

Launch the game via Steam — it will pause at a message box waiting for a debugger. Make sure you have [Visual Studio Tools for Unity](https://learn.microsoft.com/en-us/visualstudio/gamedev/unity/get-started/getting-started-with-visual-studio-tools-for-unity) installed, then in VS go to **Debug → Attach Unity Debugger** and select the game instance.

---


## Contributing

Contributions are welcome!  
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

## AI Notice

Some contributors use AI in their contributions. These models vary from ChatGPT, Gemini etc
> Personally I am not against the use of AI, I believe AI is a tool not a solution. If you're a contributor who uses AI thats fine

---

## License

This project is licensed under the MIT License.  
Copyright (c) 2025 - 2026 Luke Rapkin
