A [hookfxr](https://github.com/MonkeyModdingTroop/hookfxr) game wrapper that loads MonkeyLoader and BepInEx into Resonite at the same time.

Some notes: 

- Currently requires manual installation of BepisLoader and BepInEx mods (Does not work with mod managers such as r2modman)
- MonkeyLoader pre-patchers don't work. BepInEx ones do.
- It doesn't matter which order you do the first two installation steps.

Installation steps:

1. Install BepisLoader (https://github.com/ResoniteModding/BepisLoader)

2. Install MonkeyLoader + Resonite GamePack (and optionally the RML GamePack) (https://github.com/ResoniteModdingGroup/MonkeyLoader.GamePacks.Resonite)

3. Extract the contents of [MonkeyBepisLoader.zip](https://github.com/ResoniteModdingGroup/MonkeyBepisLoader/releases/latest/download/MonkeyBepisLoader.zip) into the root of your Resonite installation folder and say yes to overwriting any files

4. ***(Only do this step on Linux!)*** Change the steam launch options to `./run_monkeybepisloader.sh %command%`

5. Start Resonite and enjoy all the mods!

You can get BepInEx mods from the Resonite modding discord or from [Thunderstore](https://thunderstore.io/c/resonite/?section=mods).

### How to disable

- Use the Steam launch argument `--hookfxr-disable`
- Alternatively, rename or remove the `hostfxr.dll` file in the Resonite folder
