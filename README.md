# UE4CMake
Provides a simple way to add a cmake lib to any Unreal Engine 4 (UE4) project. Till I find a better way the contents of CMakeTarget.cs will need to be copied to your {Project}.Build.cs file and copy the CMakeLists.txt file next to your Build.cs file. From there you can include any (right now just supports VS generator, will add support for others as needed) cmake project. Just call the static function `CMakeTarget.add()` with the `TargetRules`, `ModuleRules`, `lib's cmake target name`, and `location of lib`.

```
public class {YourProject}:ModuleRules
{
    public {YourProject}(ReadOnlyTargetRules Target) : base(Target)
    {
        ...
        CMakeTarget.add(Target, this, "{lib's cmake target name}", {location to cmake lib source});
        ...
    }
}
```

The CMakeTarget will then create a directory in your Modules source directory under `Source/Thirdparty/generated/{LibName}`. It will generate a CMakeLists.txt file that will link to cmake libraries directory and will call cmake to generate the build files under `build` in the same `generated/{LibName}` directory. Once the cmake generation is complete it will then use cmake to build the lib and will fetch the libs includes and addtional libraries from its target name and then automatically add that to the `ModuleRules` with `PublicIncludePaths` and `PublicAdditionalLibraries`.

There is support to get the binary locations of the lib but is not currently setup. Also right now it will only work with MSVC but could be fixed to work with any cmake supported generator.