[![discord](https://img.shields.io/discord/495955797872869376.svg?logo=discord "Discord")](https://discord.gg/4AYSjfEByn)

# UE4CMake
Provides a simple way to add a cmake lib to any Unreal Engine 4 or 5 project. 

It works by setting itself up as an empty plugin, this allows the `.cs` build files to generate an Assembly that will be included into your project through the plugin system.

## Setup
Copy the contents of this repo (or git submodule it) into your UE4/5 project under `Plugins/UE4CMake`. The directory naming matters as some of the build script is hard coded to use the above naming (as there is no easy way to get the specific plugin directory otherwise). The `.uplugin` file sets up a plugin with no source, however it allows the `CMakeTarget.Build.cs` file to be compiled and included with your project.

In your UE4/5 project file `.uproject` (or if you are building a plugin it should work with your `.uplugin` file) add `CMakeTarget` as a plugin as follows
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
This generates the UE4CMake Assembly and links it with your build Assembly which will allow you to call the functions in the UE4CMake build scripts.

From there you can include any modern cmake project (older cmake may/may not have issues). Just call the static function `CMakeTarget.add()` with the `TargetRules`, `ModuleRules`, `lib's cmake target name`, `location of lib` and any `cmake args`. 

```c++
public class {YourProject}:ModuleRules
{
    public {YourProject}(ReadOnlyTargetRules Target) : base(Target)
    {
        ...
        CMakeTarget.add(Target, this, "{lib's cmake target name}", "{location to cmake lib source}", "{cmake args}", {bool, use system compiler});
        ...
    }
}
```
- {lib's cmake target name} - target name in the libraries `CMakeLists.txt` file, name provided to add_library({target name})
- {location to cmake lib source} - directory of libraries `CMakeLists.txt`, it can be relative to your projects `{Project}.Build.cs` or an absolute path (although you should generate it from something relative like, this.ModuleDirectory).
- {cmake args} - any cmake arguments you want to provide to the target, some information is pulled from the unreal build system like, `BUILD_TYPE`, `INSTALL_PATH`, `CXX_COMPILER`, and etc... but you can still override them via this argument and set any options.
- {bool, use system compiler} - optional linux only,  tells the build system to use the system compiler over the embbeded compiler in UE4/5. The embbeded compiler can be limited although it is relatively new clang version, for example even though it supports C++17 it does not include the std::filesystem library. Likely if you use this option your cmake library needs to be a shared object (.so) as static linking from a different compiler likely won't work.

## How it works

When your project build files are generated, CMakeTarget will create a directory in the Intermediate directory under `Intermediate/CMakeTarget/{LibName}`. It will generate a `CMakeLists.txt` file that will link to then added library directory. It will then call cmake to generate the build files for that library under `build` in the same `Intermediate/CMakeTarget/{LibName}` directory. Once the cmake generation is complete it will then use cmake to build the library and will fetch the library's include directoryes and additonal libraries required for the target. It will then automatically add those to the `ModuleRules` variables `PublicIncludePaths` and `PublicAdditionalLibraries`. It will also add the cmake target's `CMakeLists.txt` file and source files to `ModuleRules.ExternalDependencies` so that changes to the cmake target or it's source will outdate the UE4/5 project which will force a re-build of the cmake target. If the cmake generation or build fails it will add a non existent file to the dependencies forcing the UE4/5 build system to run cmake again on the next build. Once the cmake completes successfully the non existent file will no longer be included.

The above cmake functionality generates an output file in the `Intermediate/CMakeTarget/{LibName}/Build` directory `buildinfo_{BuildType}.ouput`, this file includes all the information that is added to the `ModuleRules`. This same directory includes all the cmake build information that is generated and will include cmake logs if you run into errors. 

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
        CMakeTarget.add(Target, this, "FastNoise", Path.Combine(this.ModuleDirectory, "../Deps/FastNoise2"), "-DFASTNOISE2_NOISETOOL=OFF", true);
        ...
    }
}
```
