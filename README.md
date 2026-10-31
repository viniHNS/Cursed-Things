<div align="center">

# CursedThings

Things that should not exist, but do: two oversized single action revolvers, a practice grenade that only pops and a hand-thrown nuke, for SPT 4.1.6.

![Version](https://img.shields.io/badge/version-1.0.0-orange?style=flat)
![SPT](https://img.shields.io/badge/SPT-4.1.6-blue?style=flat)
![WTT-CommonLib](https://img.shields.io/badge/WTT--CommonLib-3.0.6-purple?style=flat)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?style=flat&logo=dotnet)
![License](https://img.shields.io/badge/license-MIT-green?style=flat)

[Features](#features) · [Install](#install) · [New Items](#new-items) · [Configuration](#configuration) · [Build](#build-from-source)

</div>

---

## Features

**Revolvers**

- **Magnum Research BFR .50 BMG**: a stainless steel single action revolver blown up to fire the game's 12.7x99 mm (.50 BMG) rounds from a 5-round cylinder.
- **Handcannon PZ**: a heavy single action revolver in 7.62x51 mm NATO, with a 5-round cylinder.
- Both are cocked by hand before every shot and loaded through the loading gate: the cases come out one at a time and the chambers are loaded one by one.

**Grenades**

- **M69 practice hand grenade**: the M67's size, weight and handling. After the fuze delay it only pops: no blast, no fragments, no damage.
- **Tactical nuclear hand grenade**: a 15-second fuze, a lethal radius of 80 m, a fireball and a mushroom cloud. It can be planted as a tripwire.

**Client**

- **Single action reload**: the game's cylinder reload is made for swing-out cylinders: it throws every case out at once and, with weapon mastery, switches to a speedloader reload. For these two revolvers the reload goes chamber by chamber.
- **BFR arm strain**: holding up and firing the BFR tires your arms much faster than any other weapon. Configurable in the F12 menu, and it can be turned off.
- **M69 pop**: when the fuze runs out, the M69 plays the game's own grenade fuze sound (the one tripwires use).
- **Nuclear blast**: blast damage, flash, contusion, deafening and camera shake by distance, a shockwave, a fireball and a mushroom cloud about 200 m wide. The blast, rumble and echoes reach you with the shockwave, at the speed of sound, and a Geiger counter clicks while the fuze burns.

**New content**

- 2 [weapons](#weapons) and 2 [grenades](#grenades), sold by Peacekeeper and Jaeger.
- Names and descriptions in English and Portuguese.

---

## Install

Install [WTT-CommonLib](https://github.com/GrooveypenguinX/WTT-CommonLib) **3.0.6** or newer (3.0.x) first. Then extract `CursedThings.zip` into your SPT game folder:

```
<game folder>/
├── BepInEx/plugins/
│   ├── CursedThings.dll
│   └── CursedThings/nuke_fx.bundle     nuke effects and sounds
└── SPT_Runtime/user/mods/CursedThings/
    ├── CursedThings.dll
    ├── bundles.json
    ├── bundles/            models of the new items
    └── db/                 items, presets, trader offers and locales
```

Both parts are required: the server adds the items and the client handles the revolver reload, the BFR arm strain and the grenade effects.

> The new items stay in your profile. Removing the mod after buying or looting them breaks the profile.

---

## New Items

### Weapons

| Weapon | Caliber | Sold by |
|---|---|---|
| Magnum Research BFR .50 BMG | 12.7x99 mm (.50 BMG) | Peacekeeper LL4 ($1,600; limit 2) |
| Handcannon PZ | 7.62x51 mm NATO | Jaeger LL3 (85,000 ₽; limit 2) |

Both are sold with their 5-round cylinder.

### Grenades

| Grenade | Sold by |
|---|---|
| M69 practice hand grenade | Peacekeeper LL1 ($15; limit 10) |
| Tactical nuclear hand grenade | Peacekeeper LL4 ($2,000; limit 1) |

The M69 can't be planted as a tripwire.

The nuke's fuze burns for 15 seconds. Anyone within 80 m dies, cover or not. Out to 160 m the damage falls off with distance, and solid cover cuts it to 30%. The flash (if you look at it), contusion, deafening and camera shake reach 400 m. Planted as a tripwire, the fuze starts when someone trips the wire. All of these values can be changed in the [F12 menu](#configuration).

---

## Configuration

### Client (`F12` menu)

| Section | Setting | Default | Description |
|---|---|---|---|
| BFR .50 BMG | Enable Arm Strain | `true` | Holding up and firing the BFR tires your arms much faster than other weapons |
| BFR .50 BMG | Aim Stamina Drain Multiplier | `2` | How many times faster arm stamina drains while aiming the BFR (1–5) |
| BFR .50 BMG | Arm Stamina Cost Per Shot | `12` | Arm stamina taken by each shot, aiming or not; base arm stamina is 80 (0–80) |
| BFR .50 BMG | Arm Recovery Delay After Shot | `3` | Seconds after each shot before arm stamina starts to recover (0–10) |
| Nuclear Grenade | Fuse Time | `15` | Seconds from throw to detonation, and the tripwire delay; applies to the next nuke thrown (3–60) |
| Nuclear Grenade | Lethal Radius | `80` | Meters. Anyone inside dies, cover or not (5–300) |
| Nuclear Grenade | Heavy Damage Radius | `160` | Meters. Between the lethal radius and this one, damage falls off with distance; solid cover cuts it to 30% (10–600) |
| Nuclear Grenade | Shockwave Radius | `400` | Meters. Reach of the flash (if you look at it), contusion, deafening and camera shake (20–1200) |
| Nuclear Grenade | Damage Thrower | `true` | Off: the player who threw the nuke takes no blast damage (flash and contusion still apply) |
| Nuclear Grenade | Camera Shake | `true` | Shake the camera when the shockwave reaches you |
| Nuclear Grenade | Mushroom Cloud | `true` | Full blast effect (fireball, shockwave, mushroom cloud). Off: only the game's explosion and a light |
| Nuclear Grenade | Effects Quality | `High` | High: full smoke and the shockwave distortion. Low: half the smoke particles, no distortion |
| Nuclear Grenade | Sound Volume | `1` | Volume of the blast, rumble and echoes (0–1) |
| Nuclear Grenade | Geiger Counter | `true` | Geiger counter clicks from the grenade while the fuze burns, faster in the last 5 seconds |

Turning off the mushroom cloud, or setting the effects quality to Low, makes the nuke lighter on performance.

The server has no settings.

---

## Build from Source

**Requirements:** .NET 10 SDK and an SPT 4.1.6 install (the client project references the game's DLLs).

```sh
dotnet build CursedThings.sln -c Release
```

The server project builds the client first and, in Release, creates `CursedThings.zip` in the solution folder with both DLLs, `nuke_fx.bundle`, `db/`, `bundles/` and `bundles.json`.

> The client `.csproj` points at `D:\Jogos\SPT4.1` for its references (override with `-p:SptGameDir=...`), and both projects copy their build output into that install for testing. Change `SptModsDir` to your own SPT folder too. Close the SPT server and the game before building, or the copy fails because the DLLs are in use.

### Project Structure

```
CursedThings/
├── CursedThings.sln
├── Server/                             .NET 10 server mod
│   ├── Mod.cs                          metadata and WTT loading
│   ├── bundles.json                    bundle list for the new items
│   ├── bundles/                        models of the new items
│   └── db/
│       ├── CustomItems/                revolvers, cylinders and grenades (with the grenade offers)
│       ├── CustomLocales/              names of the weapon builds (EN, PO)
│       └── weaponPresets/
│           ├── Assorts/                revolvers sold by Peacekeeper and Jaeger
│           └── GlobalPresets/          default weapon builds
└── Client/                             BepInEx plugin (netstandard2.1)
    ├── Plugin.cs                       F12 settings
    ├── Assets/nuke_fx.bundle           nuke effects and sounds (prebuilt in Unity)
    ├── Nuke/
    │   ├── NukeAudio.cs                Geiger clicks, blast, rumble and echoes
    │   ├── NukeBlast.cs                blast damage, flash and contusion
    │   ├── NukeFx.cs                   fireball, shockwave and mushroom cloud
    │   └── NukeFxLoader.cs             loads nuke_fx.bundle
    └── Patches/
        ├── HeavyRevolverStrainPatch.cs             BFR arm strain
        ├── NukeGrenadePatches.cs                   nuke fuze and explosion
        ├── PracticeGrenadePopPatch.cs              M69 pop
        └── SingleActionRevolverReloadPatch.cs      chamber-by-chamber reload
```

---

## Credits

- Revolver animations and animator controller: adapted from [Peacemaker](https://sp-mod.com/mod/1267/peacemaker) by **Choccy Milk**, [SPT 4 port](https://github.com/sgtlaggy/spt-peacemaker) by **sgtlaggy**, licensed under [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). The controller was changed to a 5-chamber cylinder and the clips were adjusted to each revolver. These assets retain their CC BY 3.0 attribution; the mod's MIT license covers the code.
- Revolver handling sounds (hammer, loading gate, ejector rod): from the Peacemaker mod.
- Magnum Research BFR .50 BMG model: from the BFR50BMG H3VR mod by **Volks**.
- Handcannon PZ model: from the Handcannon PZ H3VR mod by **ShermanJumbo** (model from Resident Evil Village).
- Tactical nuclear hand grenade model: adapted from [Athom grenade](https://sketchfab.com/3d-models/athom-grenade-c6e43eb37da349e2a5d91ec90c52e167) by [BadVibes4EVER](https://sketchfab.com/BadVibes4EVER), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The body and spoon were rescaled and fitted to the game's M18 grenade (the pin and ring are the M18's), and the textures were converted to the game's materials. These assets retain their CC BY 4.0 attribution; the mod's MIT license covers the code.
- M69 practice hand grenade model: [M69 Hand Grenade and M228 Fuze](https://sketchfab.com/3d-models/m69-hand-grenade-and-m228-fuze-8487b035f785401ca4e495a84e36b952) by [MACINTOSH Plus](https://sketchfab.com/macintoshplus941) (Sketchfab Standard license).

---

## Resources

| Resource | URL |
|---|---|
| WTT-CommonLib | https://github.com/GrooveypenguinX/WTT-CommonLib |
| SPT Server C# | https://github.com/SP-Tushonka/server-csharp |
| Server Mod Examples | https://github.com/SP-Tushonka/server-mod-examples |
| SPT Wiki — Modding Resources | https://wiki.sp-tushonka.com/en/modding/Modding_Resources |
| SPT Scaffold | https://github.com/viniHNS/spt-scaffold |

---

## License

The code of this mod is licensed under the [MIT License](LICENSE). Third-party assets keep their own licenses and are not covered by the MIT License (see [Credits](#credits)): the Peacemaker animations are CC BY 3.0 and the nuclear grenade model is CC BY 4.0.
