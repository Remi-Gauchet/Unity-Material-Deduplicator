# Scarlet Material Deduplicator

A Unity Editor tool that finds **strictly identical Materials** in your project, replaces every reference to the duplicates with a single Material, and lets you safely delete the leftovers.

It is especially useful after using **Extract Materials** on imported 3D models (FBX, GLB...), which often produces many identical `.mat` files across asset packs.

## Features

- **Scan** the `Assets` folder for duplicate Materials without modifying anything.
- **Merge** duplicates in a single pass: references are updated in model import settings, prefabs, scenes and ScriptableObjects.
- **Safe deletion**: a separate button moves duplicates to the trash, and refuses to do so if anything still references them.
- Works with any render pipeline (Built-in, URP, HDRP).

## Requirements

- Unity 2021.2 or newer (developed and tested with Unity 6.3, URP).

## Installation

1. Download `Assets/Editor/UnityMaterialDeduplicator.cs` from this repository (or clone the repository / download it as a ZIP).
2. In your Unity project, create a folder named `Editor` inside `Assets` (if it does not exist yet).
3. Copy `UnityMaterialDeduplicator.cs` into your project's `Assets/Editor/` folder.
4. Wait for Unity to recompile.

## Usage

> **Back up first.** The merge step modifies many files at once and cannot be undone with Ctrl+Z. Commit your work to a Git branch (including the `.meta` files) before running it.

1. Open **Tools > Scarlet > Material Deduplicator**.
2. Click **1. SCAN PROJECT**. The window lists each group of duplicates: the Material that will be kept, and the duplicates that will be replaced.
3. Review the groups.
4. Click **2. MERGE DUPLICATES**. Unity offers to save your open scenes, then asks for confirmation. Models are reimported, which can take a while on large projects.
5. Check your project: nothing should be pink and everything should look the same as before.
6. Click **3. TRASH DUPLICATES** to delete the now-unused duplicates.
7. Commit the result.

### Recommended workflow after importing a model pack

1. Select the model, then in the Inspector: **Materials > Extract Materials**.
2. Reconnect the textures and fix the normal maps.
3. Run the deduplicator.

Run it only **after** the textures are connected. Freshly extracted Materials are empty and therefore all identical, so they would be wrongly merged together.

## How it works

### Detecting duplicates

Each `.mat` file in `Assets` gets a signature built from:

- the shader, render queue, GPU instancing, double-sided GI and GI flags
- the enabled shader keywords
- every shader property: colors, vectors, floats/ranges, integers, and textures (including offset and scale)

Floating-point values are rounded to 5 decimals. Materials sharing the same signature are duplicates.

Materials embedded inside model files (FBX, GLB...) are ignored, because they are not separate `.mat` assets and cannot be replaced or deleted.

### Choosing the Material to keep

In each group, the Material with the **shortest name** is kept (so `Wood` wins over `Wood 1`), then the first one in alphabetical order of its path.

### What gets updated during a merge

| Asset type | Update |
| --- | --- |
| Models (`.fbx`, `.glb`, `.gltf`, `.obj`, `.blend`, `.dae`, `.3ds`) | External object map (the Materials remapping in Import Settings) |
| Prefabs | All Material references on every component, including arrays such as `MeshRenderer.materials` |
| Scenes | Same as prefabs (scenes are opened, saved and closed automatically) |
| ScriptableObject assets | All Material references |

Only assets that directly depend on a duplicate are opened, so the process stays fast.

## Limitations

- Only the `Assets` folder is processed. Materials from packages are never touched.
- The **Skybox** Material set in a scene's lighting settings is not updated.
- References resolved at runtime are not detected: `Resources.Load` by name or path, Addressables addresses, or Materials referenced by path in scripts.
- Project files other than the types listed above (for example custom asset formats) are not updated. The trash step will detect remaining direct references and refuse to delete in that case.

## Troubleshooting

**Some Materials are pink after the merge.**
Restore your branch (`git restore .` or `git checkout .`), and check whether the affected Materials are referenced in one of the unsupported ways listed above.

**The tool finds fewer duplicates than expected.**
Duplicates must be strictly identical, including textures, offsets and keywords. Materials embedded in model files are not counted.

**The menu entry does not appear.**
Make sure the script is inside a folder named `Editor` and that the Console shows no compile errors.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
