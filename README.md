[![discord](https://img.shields.io/discord/495955797872869376.svg?logo=discord "Discord")](https://discord.gg/4AYSjfEByn)

# UE4CMake
Provides a simple way to add a cmake lib to any Unreal Engine 4 (UE4) project. 

This might be a bit of a hack so may not work as the UE4 engine changes. Since there is no easy way to include a `.cs` file into the current build system (as far as I a can tell) this project sets itself up as a empty plugin.

Copy the contents of this repo (or git submodule it) into your UE4 project under `Plugins/CMakeTarget`. The `.uplugin` file will setup the code as a plugin and the `.Build.cs` file sets up an empty plugin (but at least the `.cs` file is built as an Assembly).

In your UE4 project file `.uproject` (or if you are building a plugin it should work with your `.uplugin` file) add `CMakeTarget` as a plugin like follows
```
    "FileVersion": 3,
    ...
    "Plugins": [
		{
			"Name": "CMakeTarget",
			"Enabled": true
		}
	]
```
This will force the plugin to be built (again just need the Assembly built from the `.cs` file) and will include the plugin's Assembly into your build scripts.

From there you can include any modern cmake project (older cmake may/may not have issues). Just call the static function `CMakeTarget.add()` with the `TargetRules`, `ModuleRules`, `lib's cmake target name`, `location of lib` and any `cmake args`.

```c++
public class {YourProject}:ModuleRules
{
    public {YourProject}(ReadOnlyTargetRules Target) : base(Target)
    {
        ...
        CMakeTarget.add(Target, this, "{lib's cmake target name}", "{location to cmake lib source}", "{cmake args}");
        ...
    }
}
```

The CMakeTarget will then create a directory in your Intermediate directory under `Intermediate/CMakeTarget/{LibName}`. It will generate a CMakeLists.txt file that will link to cmake libraries directory and will call cmake to generate the build files under `build` in the same `Intermediate/CMakeTarget/{LibName}` directory. Once the cmake generation is complete it will then use cmake to build the lib and will fetch the libs includes and addtional libraries from its target name and then automatically add that to the `ModuleRules` with `PublicIncludePaths` and `PublicAdditionalLibraries`. It will also add the cmake target's source/CMakeLists files to `ModuleRules.ExternalDependencies` so that changes to the cmake target will outdate the UE4 project which will force a build of the cmake target. If the cmake generation or build fails it will add a non existent file to the dependencies forcing cmake run again on the next build. Once the cmake completes successfully the non existent file will no longer be included.

There is support to get the binary locations of the lib but is not currently setup.

## Example
[FastNoise2](https://github.com/caseymcc/UE4_FastNoise2)

### FastNoise2Example.uproject:
Original
```c++
{
	"FileVersion": 3,
	"EngineAssociation": "4.25",
	"Category": "",
	"Description": "",
	"Modules": [
		{
			"Name": "FastNoise2Example",
			"Type": "Runtime",
			"LoadingPhase": "Default"
		}
	]
}
```
to
```c++
{
	"FileVersion": 3,
	"EngineAssociation": "4.25",
	"Category": "",
	"Description": "",
	"Modules": [
		{
			"Name": "FastNoise2Example",
			"Type": "Runtime",
			"LoadingPhase": "Default"
		}
	],
	"Plugins": [
		{
			"Name": "CMakeTarget",
			"Enabled": true
		}
	]
}
```

### FastNoise2Example.Build.cs:
Original
```c++
using UnrealBuildTool;

public class FastNoise2Example : ModuleRules
{
	public FastNoise2Example(ReadOnlyTargetRules Target) : base(Target)
	{
        ...
    }
}
```
to
```c++
using UnrealBuildTool;

public class FastNoise2Example : ModuleRules
{
	public FastNoise2Example(ReadOnlyTargetRules Target) : base(Target)
	{
        ...
        CMakeTarget.add(Target, this, "FastNoise", Path.Combine(this.ModuleDirectory, "../Deps/FastNoise2"), "-DFASTNOISE2_NOISETOOL=OFF");
        ...
    }
}
```
