
using UnrealBuildTool;
using EpicGames.Core;
using System;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class DateTimeExtensions
{
    public static bool EqualsUpToSeconds(this DateTime dt1, DateTime dt2)
    {
        return dt1.Year == dt2.Year && dt1.Month == dt2.Month && dt1.Day == dt2.Day &&
               dt1.Hour == dt2.Hour && dt1.Minute == dt2.Minute && dt1.Second == dt2.Second;
    }   
}

public class GeneratorInfo
{
    public GeneratorInfo(string name, string options)
    {
        m_name=name;
        m_options=options;
        m_cCompiler="";
        m_cppCompiler="";
        m_linker="";
    }

    public GeneratorInfo(string name, string options, string cCompiler, string cppCompiler, string linker)
    {
        m_name=name;
        m_options=options;
        m_cCompiler=cCompiler;
        m_cppCompiler=cppCompiler;
        m_linker=linker;
    }

    public string m_name;
    public string m_options;
    public string m_cCompiler;
    public string m_cppCompiler;
    public string m_linker;
}

public class CMakeTargetInst
{
    private string m_cmakeTargetPath;
    private string m_modulePath;
    private string m_targetName;
    private string m_targetLocation;
    private string m_targetPath;
    private string m_cmakeArgs;
//    private string[] m_includeDirectories;
//    private string[] m_libraries;

    private string m_buildDirectory;
    private string m_buildPath;
    private string m_generatedTargetPath;

    private string m_thirdPartyGeneratedPath;

    private string m_buildInfoFile;
    private string m_buildInfoPath;

    private bool m_forceBuild=false;
    private string m_forceBuildType;

    private bool m_includedToolchain=false;
    private string m_includedToolchainPath;

    public CMakeTargetInst(string targetName, string targetLocation, string args)
    {
        m_targetName=targetName;
        m_targetLocation=targetLocation;

        Regex buildTypeRegex=new Regex(@"-DCMAKE_BUILD_TYPE=(\w*)");
        Match buildTypeMatch=buildTypeRegex.Match(args);

        if(buildTypeMatch.Success && (buildTypeMatch.Groups.Count > 1))
        {
            m_forceBuild=true;
            m_forceBuildType=buildTypeMatch.Groups[1].Value;
        }

        //check for toolchain file as we need to copy its contents
        Regex toolChainRegex=new Regex(@"-DCMAKE_TOOLCHAIN_FILE=([\\\/.\w]*)");
        Match toolChainMatch=toolChainRegex.Match(args);

        if(toolChainMatch.Success && (toolChainMatch.Groups.Count > 1))
        {
            m_includedToolchain=true;
            m_includedToolchainPath=toolChainMatch.Groups[1].Value;
            args=toolChainRegex.Replace(args, @"");
        }

        m_cmakeArgs=args;
    }

    public bool addRules(ModuleRules rules, bool useSystemCompiler)
    {
        Console.WriteLine("Loading build info file: "+m_buildInfoPath);

        if(!File.Exists(m_buildInfoPath))
        {
            Console.WriteLine("Failed loading: "+m_buildInfoPath);
            return false;
        }

        Dictionary<string, string> values = new Dictionary<string, string>();

        StreamReader reader = new System.IO.StreamReader(m_buildInfoPath);
        string line = null;

        while((line=reader.ReadLine())!=null)
        {
            string[] tokens = line.Split('=');

            if(tokens.Length!=2)
                continue;

            values.Add(tokens[0], tokens[1]);
        }

        if(values.ContainsKey("cppStandard"))
        {
            string standard = values["cppStandard"];

            if(!String.IsNullOrEmpty(standard))
            {
                if(standard.Equals("11"))
                    rules.CppStandard=CppStandardVersion.Default;
                else if(standard.Equals("14"))
                    rules.CppStandard=CppStandardVersion.Cpp14;
                else if(standard.Equals("17"))
                    rules.CppStandard=CppStandardVersion.Cpp17;
                else if(standard.Equals("20"))
                    rules.CppStandard=CppStandardVersion.Cpp20;
                else
                    rules.CppStandard=CppStandardVersion.Latest;

                if((!useSystemCompiler) && (rules.Target.Platform == UnrealTargetPlatform.Linux))
                    rules.PublicSystemLibraries.Add("stdc++");
            }
        }

        if(values.ContainsKey("dependencies"))
        {
            string[] dependencies = values["dependencies"].Split(',');

            foreach(string depend in dependencies)
            {
                if(String.IsNullOrEmpty(depend))
                    continue;
                    
                rules.ExternalDependencies.Add(depend);
            }
        }

        if(values.ContainsKey("sourceDependencies"))
        {
            string sourcePath="";

            if(values.ContainsKey("sourcePath"))
                sourcePath=values["sourcePath"];

            string[] dependencies = values["sourceDependencies"].Split(',');

            foreach(string depend in dependencies)
            {
                if(String.IsNullOrEmpty(depend))
                    continue;

                string dependPath=Path.Combine(sourcePath, depend);

                rules.ExternalDependencies.Add(dependPath);
            }
        }

        if(values.ContainsKey("includes"))
        {
            string[] includes = values["includes"].Split(',');

            foreach(string include in includes)
            {
                if(String.IsNullOrEmpty(include))
                    continue;

                rules.PublicIncludePaths.Add(include);
            }
        }

        if(values.ContainsKey("binaryDirectories"))
        {
            string[] binaryDirectories = values["binaryDirectories"].Split(',');

            foreach(string binaryDirectory in binaryDirectories)
            {
                if(String.IsNullOrEmpty(binaryDirectory))
                    continue;

                Console.WriteLine("Add library path: "+binaryDirectory);
                rules.PublicRuntimeLibraryPaths.Add(binaryDirectory);
            }
        }

        if(values.ContainsKey("libraries"))
        {
            string[] libraries = values["libraries"].Split(',');

            foreach(string library in libraries)
            {
                if(String.IsNullOrEmpty(library))
                    continue;

                rules.PublicAdditionalLibraries.Add(library);
            }
        }

        return true;
    }

    public void AddFailed(ModuleRules rules)
    {
        string dummyFile=Path.Combine(m_targetPath, "build.failed");

        //adding non existent file to debpendencies to force cmake to re-run
        rules.ExternalDependencies.Add(dummyFile);
    }

    private string GetBuildType(ReadOnlyTargetRules target)
    {
        string buildType = "Release";

        if(m_forceBuild)
            return m_forceBuildType;

        switch(target.Configuration)
        {
            case UnrealTargetConfiguration.Debug:
            case UnrealTargetConfiguration.DebugGame:
                buildType="Debug";
                break;
            case UnrealTargetConfiguration.Development:
            default:
                break;
        }

        return buildType;
    }

    public bool Load(ReadOnlyTargetRules target, ModuleRules rules, bool useSystemCompiler)
    {
        string buildType = GetBuildType(target);

        Console.WriteLine("Loading cmake target: "+target);

        m_cmakeTargetPath=Path.GetFullPath(rules.Target.ProjectFile.FullName);
        m_cmakeTargetPath=Directory.GetParent(m_cmakeTargetPath).FullName+"/Plugins/UE4CMake/Source";

        m_modulePath=Path.GetFullPath(rules.ModuleDirectory);
        m_targetPath=Path.Combine(m_modulePath, m_targetLocation);

        m_thirdPartyGeneratedPath=Path.Combine(rules.Target.ProjectFile.Directory.FullName, "Intermediate", "CMakeTarget");
        m_generatedTargetPath=Path.Combine(m_thirdPartyGeneratedPath, m_targetName);
        m_buildDirectory="build";
        m_buildPath=Path.Combine(m_generatedTargetPath, m_buildDirectory);

        m_buildInfoFile="buildinfo_"+buildType+".output";
        m_buildInfoPath=Path.Combine(m_buildPath, m_buildInfoFile).Replace("\\", "/");
        
        if(!Directory.Exists(m_generatedTargetPath))
            Directory.CreateDirectory(m_generatedTargetPath);

        if(!Directory.Exists(m_buildPath))
            Directory.CreateDirectory(m_buildPath);

        var moduleBuilt = Build(target, rules, buildType, useSystemCompiler);

        if(!moduleBuilt)
        {
            return false;
        }
        return true;
    }

    private bool Build(ReadOnlyTargetRules target, ModuleRules rules, string buildType, bool useSystemCompiler)
    {
        string builtFile = Path.Combine(m_generatedTargetPath, buildType+".built");
        string projectCMakeLists=Path.GetFullPath(Path.Combine(m_targetPath, "CMakeLists.txt"));

        bool configCMake=true;

        //check if already built and CMakeList.txt not changed
        if(File.Exists(builtFile))
        {
            DateTime cmakeLastWrite=File.GetLastWriteTime(projectCMakeLists);
            string builtTimeString=System.IO.File.ReadAllText(builtFile);
            DateTime builtTime=DateTime.Parse(builtTimeString);

            if(builtTime.EqualsUpToSeconds(cmakeLastWrite))
                configCMake=false;
        }

        if(configCMake)
        {
            Console.WriteLine("Target "+m_targetName+" CMakeLists.txt out of date, rebuilding");

            var configureCommand = CreateCMakeConfigCommand(target, rules, m_buildPath, buildType, useSystemCompiler);
            var configureCode = ExecuteCommandSync(configureCommand);

            if(configureCode!=0)
            {
                Console.WriteLine("Cannot configure CMake project. Exited with code: "
                    +configureCode);
                return false;
            }
        }

        var buildCommand = CreateCMakeBuildCommand(m_buildPath, buildType);
        var buildCode = ExecuteCommandSync(buildCommand);

        if(buildCode!=0)
        {
            Console.WriteLine("Cannot build project. Exited with code: "+buildCode);
            return false;
        }
        else
        {
            if(configCMake)
            {
                DateTime cmakeLastWrite=File.GetLastWriteTime(projectCMakeLists);

                File.WriteAllText(builtFile, cmakeLastWrite.ToString());
            }
        }
        return true;
    }

    private string GetWindowsGeneratorName(WindowsCompiler compiler)
    {
        string generatorName="";

        switch(compiler)
        {
        case WindowsCompiler.Default:
        break;
        case WindowsCompiler.Clang:
            generatorName="NMake Makefiles";
        break;
        case WindowsCompiler.Intel:
            generatorName="NMake Makefiles";
        break;
#if !UE_5_0_OR_LATER
        case WindowsCompiler.VisualStudio2017:
            generatorName="Visual Studio 15 2017";
        break;
#endif//!UE_5_0_OR_LATER
        case WindowsCompiler.VisualStudio2019:
            generatorName="Visual Studio 16 2019";
        break;
        case WindowsCompiler.VisualStudio2022:
            generatorName="Visual Studio 17 2022";
        break;
        }

        return generatorName;
    }

#if UE_5_2_OR_LATER   // UE 5.2 and onwards
    private string GetWindowsGeneratorOptions(WindowsCompiler compiler, UnrealArch architecture)
#else
    private string GetWindowsGeneratorOptions(WindowsCompiler compiler, WindowsArchitecture architecture)
#endif
    {
        string generatorOptions="";

        if((compiler == WindowsCompiler.VisualStudio2022) || (compiler == WindowsCompiler.VisualStudio2019)
#if !UE_5_0_OR_LATER
            || (compiler == WindowsCompiler.VisualStudio2017)
#endif//!UE_5_0_OR_LATER 
        )
        {
#if UE_5_2_OR_LATER   // UE 5.2 and onwards
            if(architecture == UnrealArch.X64)
                generatorOptions="-A x64";
            else if(architecture == UnrealArch.Arm64)
                generatorOptions="-A ARM64";
#elif UE_5_0_OR_LATER // UE 5.0 to 5.1
            if(architecture == WindowsArchitecture.x64)
                generatorOptions="-A x64";
            else if(architecture == WindowsArchitecture.ARM64)
                generatorOptions="-A ARM64";

#else                 // Everything before UE 5.0
            else if(architecture == WindowsArchitecture.x86)
                generatorOptions="-A Win32";
            else if(architecture == WindowsArchitecture.ARM32)
                generatorOptions="-A ARM";
#endif
        }
        return generatorOptions;
    }

    public static bool ShouldEnableOptimization(ModuleRules.CodeOptimization Setting, UnrealTargetConfiguration Configuration, bool bIsEngineModule)
    {
        switch(Setting)
        {
            case ModuleRules.CodeOptimization.Never:
                return false;
            case ModuleRules.CodeOptimization.Default:
            case ModuleRules.CodeOptimization.InNonDebugBuilds:
                return (Configuration == UnrealTargetConfiguration.Debug)? false : (Configuration != UnrealTargetConfiguration.DebugGame || bIsEngineModule);
            case ModuleRules.CodeOptimization.InShippingBuildsOnly:
                return (Configuration == UnrealTargetConfiguration.Shipping);
            default:
                return true;
        }
    }
    private string GetClangGeneratorOptions(ReadOnlyTargetRules target, ModuleRules rules, string sdkDirectory)
    {
        string cxxOptions="";
        string generatorOptions="";

        bool bOptimizeCode = ShouldEnableOptimization(rules.OptimizeCode, target.Configuration, rules.bTreatAsEngineModule);

//        if(!bOptimizeCode)
//        {
//            cxxOptions+="-O0";
//        }
//        else
//        {
//            // Don't over optimise if using Address/MemorySanitizer or you'll get false positive errors due to erroneous optimisation of necessary Address/MemorySanitizer instrumentation.
////            if (Options.HasFlag(ClangToolChainOptions.EnableAddressSanitizer) || Options.HasFlag(ClangToolChainOptions.EnableMemorySanitizer))
////            {
////                cxxOptions+="-O1 -g ";
////
////                // This enables __asan_default_options() in UnixCommonStartup.h which disables the leak detector
////                generatorOptions+="-DDISABLE_ASAN_LEAK_DETECTOR=1 ";
////            }
////            else if (Options.HasFlag(ClangToolChainOptions.EnableThreadSanitizer))
////            {
////                cxxOptions+="-O1 -g ";
////            }
////            else
//            {
//                if (target.OptimizationLevel == OptimizationMode.Size)
//                {
//                    cxxOptions+="-Oz ";
//                }
//                else if (target.OptimizationLevel == OptimizationMode.SizeAndSpeed)
//                {
//                    cxxOptions+="-Os ";
//                    if (target.Architecture.StartsWith("aarch64"))
//                    {
//                        cxxOptions+="-moutline ";
//                    }
//                }
//                else
//                {
//                    cxxOptions+="-O3 ";
//                }
//            }
//        }
//
//        bool bRetainFramePointers = target.bRetainFramePointers
////            || Options.HasFlag(ClangToolChainOptions.EnableAddressSanitizer) || Options.HasFlag(ClangToolChainOptions.EnableMemorySanitizer)
//            || target.Configuration == UnrealTargetConfiguration.Debug;
//
//        if (target.Configuration == UnrealTargetConfiguration.Shipping)
//        {
//            if (!bRetainFramePointers)
//            {
//                cxxOptions+="-fomit-frame-pointer ";
//            }
//        }
//        // switches to help debugging
//        else if (target.Configuration == UnrealTargetConfiguration.Debug)
//        {
//            cxxOptions+="-fno-inline ";                   // disable inlining for better debuggability (e.g. callstacks, "skip file" in gdb)
//            cxxOptions+="-fstack-protector ";             // detect stack smashing
//        }
//
//        if (bRetainFramePointers)
//        {
//            cxxOptions+="-fno-optimize-sibling-calls ";
//            cxxOptions+="-fno-omit-frame-pointer ";
//        }
//
////        if (CompilerVersionGreaterOrEqual(12, 0, 0))
//        {
//            cxxOptions+="-fbinutils-version=2.36 ";
//        }
//
//        cxxOptions+="-fno-math-errno ";
//        if (target.Architecture.StartsWith("x86_64"))
//        {
//            cxxOptions+="-mssse3 "; // enable ssse3 by default for x86. This is default on for MSVC so lets reflect that here
//        }
//
////        if (target.bShouldCompileAsDLL)
//        if(target.BuildEnvironment == TargetBuildEnvironment.Shared)
//        {
//            cxxOptions+="-fPIC ";
//            // Use local-dynamic TLS model. This generates less efficient runtime code for __thread variables, but avoids problems of running into
//            // glibc/ld.so limit (DTV_SURPLUS) for number of dlopen()'ed DSOs with static TLS (see e.g. https://www.cygwin.com/ml/libc-help/2013-11/msg00033.html)
//            cxxOptions+="-ftls-model=local-dynamic ";
//        }
//        else
//        {
//            cxxOptions+="-ffunction-sections ";
//            cxxOptions+="-fdata-sections ";
//        }
//
////        if (bSuppressPIE && !target.bShouldCompileAsDLL)
////        {
////            cxxOptions+="-fno-PIE ";
////        }
//
//        //if(target.bPGOOptimize)
//        //    generatorOptions+=" -DCMAKE_INTERPROCEDURAL_OPTIMIZATION=TRUE";
//        //else 
//        if(target.bPGOProfile)
//            cxxOptions+="-fprofile-generate ";
//
//        if(!target.bUseInlining)
//            cxxOptions+="-fno-inline-functions ";
//
//        if(rules.bUseRTTI)
//            cxxOptions+="-frtti ";
//        else
//            cxxOptions+="-fno-rtti ";
//
////        if(target.bEnableExceptions)
////        if(target.bForceEnableExceptions)
//        {
//            cxxOptions+="-fexceptions ";
//            generatorOptions+="-DPLATFORM_EXCEPTIONS_DISABLED=0 ";
//        }
////        else
////        {
////            cxxOptions+="-fno-exceptions ";
////            generatorOptions+="-DPLATFORM_EXCEPTIONS_DISABLED=1 ";
////        }
        cxxOptions+=" -Wall";
        cxxOptions+=" -Werror";
        cxxOptions+=" -Wdelete-non-virtual-dtor";
        cxxOptions+=" -Wenum-conversion";
        cxxOptions+=" -Wbitfield-enum-conversion";
        cxxOptions+=" -Wno-enum-enum-conversion";
        cxxOptions+=" -Wno-enum-float-conversion";
        cxxOptions+=" -Wno-unused-but-set-variable";
        cxxOptions+=" -Wno-unused-but-set-parameter";
        cxxOptions+=" -Wno-ordered-compare-function-pointers";
        cxxOptions+=" -Wno-gnu-string-literal-operator-template";
        cxxOptions+=" -Wno-inconsistent-missing-override";
        cxxOptions+=" -Wno-invalid-offsetof";
        cxxOptions+=" -Wno-switch";
        cxxOptions+=" -Wno-tautological-compare";
        cxxOptions+=" -Wno-unknown-pragmas";
        cxxOptions+=" -Wno-unused-function";
        cxxOptions+=" -Wno-unused-lambda-capture";
        cxxOptions+=" -Wno-unused-local-typedef";
        cxxOptions+=" -Wno-unused-private-field";
        cxxOptions+=" -Wno-unused-variable";
        cxxOptions+=" -Wno-undefined-var-template";
        cxxOptions+=" -Wshadow";
//        cxxOptions+=" -Wundef";
        cxxOptions+=" -Wno-float-conversion";
        cxxOptions+=" -Wno-implicit-float-conversion";
        cxxOptions+=" -Wno-implicit-int-conversion";
        cxxOptions+=" -Wno-c++11-narrowing";
        cxxOptions+=" -fdiagnostics-absolute-paths";
        cxxOptions+=" -fdiagnostics-color";
//        cxxOptions+=" -Wno-undefined-bool-conversion";
        cxxOptions+=" -O3";
        cxxOptions+=" -fexceptions";
        cxxOptions+=" -DPLATFORM_EXCEPTIONS_DISABLED=0";
        cxxOptions+=" -gdwarf-4";
        cxxOptions+=" -ggnu-pubnames";
        cxxOptions+=" -fvisibility-ms-compat";
        cxxOptions+=" -fvisibility-inlines-hidden";
        cxxOptions+=" -nostdinc++";
        cxxOptions+=" -isystem\""+rules.EngineDirectory+"/Source/"+target.UEThirdPartySourceDirectory+"Unix/LibCxx/include\"";
        cxxOptions+=" -isystem\""+rules.EngineDirectory+"/Source/"+target.UEThirdPartySourceDirectory+"Unix/LibCxx/include/c++/v1\"";
        cxxOptions+=" -fbinutils-version=2.36";
        cxxOptions+=" -fno-math-errno";
        cxxOptions+=" -fno-rtti";
        cxxOptions+=" -mssse3";
        cxxOptions+=" -fPIC";
        cxxOptions+=" -ftls-model=local-dynamic";
        cxxOptions+=" -D_LINUX64";
        cxxOptions+=" -target x86_64-unknown-linux-gnu";
        cxxOptions+=" --sysroot=\""+sdkDirectory+"\"";
//        cxxOptions+=" -x c++";
        cxxOptions+=" -std=c++17";
        cxxOptions+=" -fpch-validate-input-files-content";

        if(cxxOptions.Length>0)
            generatorOptions+="-DCMAKE_CXX_FLAGS=\""+cxxOptions+"\" ";

        Console.WriteLine("Clang generatorOptions: "+generatorOptions);
        return generatorOptions;
    }

    GeneratorInfo GetGeneratorInfo(ReadOnlyTargetRules target, ModuleRules rules)
    {
        string name;
        string options;
        string cCompilerPath="";
        string cppCompilerPath="";
        string linkerPath="";

        if((target.Platform == UnrealTargetPlatform.Win64) 
#if !UE_5_0_OR_LATER
            || (target.Platform == UnrealTargetPlatform.Win32)
#endif//!UE_5_0_OR_LATER
            )
        {
            name=GetWindowsGeneratorName(target.WindowsPlatform.Compiler);
            options=GetWindowsGeneratorOptions(target.WindowsPlatform.Compiler, target.WindowsPlatform.Architecture);
        }
        else if(IsUnixPlatform(target.Platform))
        {
            name="Unix Makefiles";
            options="";

            UEBuildPlatformSDK? buildSdk=UEBuildPlatformSDK.GetSDKForPlatform(target.Platform.ToString());

            if(buildSdk != null)
            {
                string? internalSDKPath = buildSdk.GetInternalSDKPath();

                if(!string.IsNullOrEmpty(internalSDKPath))
                {
                    cCompilerPath=Path.Combine(internalSDKPath, "bin", "clang");
                    cppCompilerPath=Path.Combine(internalSDKPath, "bin", "clang++");
                    linkerPath=Path.Combine(internalSDKPath, "bin", "lld");

                    options=GetClangGeneratorOptions(target, rules, internalSDKPath);
                }
            }
        }
        else
        {
            name="";
            options="";
        }

        return new GeneratorInfo(name, options, cCompilerPath, cppCompilerPath, linkerPath);
    }

    private string GetCMakeExe()
    {
        string program = "cmake";

        if((BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win64) 
#if !UE_5_0_OR_LATER
            || (BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win32)
#endif//!UE_5_0_OR_LATER
            )
        {
            program+=".exe";
        }
        return program;
    }

    private string CreateCMakeConfigCommand(ReadOnlyTargetRules target, ModuleRules rules, string buildDirectory, string buildType, bool useSystemCompiler)
    {
        string program = GetCMakeExe();
        string options = "";

        if((BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win64) 
#if !UE_5_0_OR_LATER
            || (BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win32)
#endif//!UE_5_0_OR_LATER
            )
        {
            options=" -T host=x64";
        }
        

        var generatorInfo=GetGeneratorInfo(target, rules);

        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");
        string toolchainPath = Path.Combine(m_generatedTargetPath, "toolchain.cmake");

        generateCMakeFile(target, cmakeFile, buildType);
        
        string toolChain="";
        if(generateToolchain(target, generatorInfo, toolchainPath, useSystemCompiler))
        {
            toolChain=" -DCMAKE_TOOLCHAIN_FILE=\""+toolchainPath+"\"";
        }

        if(!String.IsNullOrEmpty(generatorInfo.m_cCompiler) && !useSystemCompiler)
        {
            options+=" -DCMAKE_C_COMPILER="+generatorInfo.m_cCompiler;
            options+=" -DCMAKE_CXX_COMPILER="+generatorInfo.m_cppCompiler;
        }

        var installPath = m_thirdPartyGeneratedPath;

        var arguments = " -G \""+generatorInfo.m_name+"\""+
                        " "+generatorInfo.m_options+" "+
                        " -S \""+m_generatedTargetPath+"\""+
                        " -B \""+buildDirectory+"\""+
                        " -DCMAKE_BUILD_TYPE="+GetBuildType(target)+
                        " -DCMAKE_INSTALL_PREFIX=\""+installPath+"\""+
                        toolChain+
                        options+
                        " "+m_cmakeArgs;

        Console.WriteLine("CMakeTarget calling cmake with: "+arguments);

        return program+arguments;
    }

    private void addIncludedToolchain(ref string contents)
    {
        if(!m_includedToolchain)
            return;

        string toolChainContents = File.ReadAllText(m_includedToolchainPath);
           
        if(String.IsNullOrEmpty(toolChainContents))
            return;

        contents+=toolChainContents;
    }

    private void generateWindowsToolchain(ReadOnlyTargetRules target, string path)
    {
        string templateFilePath = Path.Combine(m_cmakeTargetPath, "toolchains/windows_toolchain.in");
        string contents = File.ReadAllText(templateFilePath);
        bool forceReleaseRuntime=true;

        if(m_forceBuild)
        {
            if(!m_forceBuildType.Equals("Release"))
                forceReleaseRuntime=false;
        }
        else
        {
            if((target.Configuration == UnrealTargetConfiguration.Debug) && (target.bDebugBuildsActuallyUseDebugCRT))
                forceReleaseRuntime=false;
        }
        contents=contents.Replace("@FORCE_RELEASE_RUNTIME@", forceReleaseRuntime?"ON":"OFF");

        addIncludedToolchain(ref contents);

        File.WriteAllText(path, contents);
    }

    private void generateUnixToolchain(ReadOnlyTargetRules target, GeneratorInfo generatorInfo, string path, bool useSystemCompiler)
    {
        string templateFilePath = Path.Combine(m_cmakeTargetPath, "toolchains/unix_toolchain.in");
        string contents = File.ReadAllText(templateFilePath);

        if(useSystemCompiler)
            contents=contents.Replace("@USE_COMPILER@", "0");
        else
            contents=contents.Replace("@USE_COMPILER@", "1");

        contents=contents.Replace("@COMPILER@", generatorInfo.m_cCompiler);
        contents=contents.Replace("@CPPCOMPILER@", generatorInfo.m_cppCompiler);
        contents=contents.Replace("@LINKER@", generatorInfo.m_linker);
        
        addIncludedToolchain(ref contents);

        File.WriteAllText(path, contents);
    }

    private bool generateToolchain(ReadOnlyTargetRules target, GeneratorInfo generatorInfo, string path, bool useSystemCompiler)
    {
        if(target.Platform == UnrealTargetPlatform.Win64)
        {
            generateWindowsToolchain(target, path);
            return true;
        }
        else if(IsUnixPlatform(target.Platform))
        {
            generateUnixToolchain(target, generatorInfo, path, useSystemCompiler);
            return true;
        }
        else
        {
            if(m_includedToolchain)
            {
                path=m_includedToolchainPath;
            }
        }
        return false;
    }

    private bool generateCMakeFile(ReadOnlyTargetRules target, string path, string buildType)
    {
        string templateFilePath = Path.Combine(m_cmakeTargetPath, "CMakeLists.in");
        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");
        const string buildDir = "build";//just one for visual studio generator

        string contents = File.ReadAllText(templateFilePath);

        bool forceReleaseRuntime=false;

        if(target.Platform == UnrealTargetPlatform.Win64)
        {
            if(buildType == "Debug")
            {
                forceReleaseRuntime=true;
                
                if((target.Configuration == UnrealTargetConfiguration.Debug) && (target.bDebugBuildsActuallyUseDebugCRT))
                    forceReleaseRuntime=false;
            }
        }

        contents=contents.Replace("@FORCE_RELEASE_RUNTIME@", forceReleaseRuntime?"ON":"OFF");
        contents=contents.Replace("@BUILD_TARGET_NAME@", m_targetName);
        contents=contents.Replace("@BUILD_TARGET_DIR@", m_targetPath.Replace("\\", "/"));
        contents=contents.Replace("@BUILD_TARGET_THIRDPARTY_DIR@", m_thirdPartyGeneratedPath.Replace("\\", "/"));
        contents=contents.Replace("@BUILD_TARGET_BUILD_DIR@", buildDir.Replace("\\", "/"));

        File.WriteAllText(cmakeFile, contents);

        return true;
    }

    private string CreateCMakeBuildCommand(string buildDirectory, string buildType)
    {
        return GetCMakeExe()+" --build \""+buildDirectory+"\" --config "+buildType;
    }

    private string CreateCMakeInstallCommand(string buildDirectory, string buildType)
    {
        return GetCMakeExe()+" --build \""+buildDirectory+"\" --target install --config "+buildType;
    }

    private Tuple<string, string> GetExecuteCommandSync()
    {
        string cmd = "";
        string options = "";

        if((BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win64) 
#if !UE_5_0_OR_LATER
            || (BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Win32)
#endif//!UE_5_0_OR_LATER
            )
        {
            cmd="cmd.exe";
            options="/c ";
        }
        else if(IsUnixPlatform(BuildHostPlatform.Current.Platform)) 
        {
            cmd="bash";
            options="-c ";
        }
        return Tuple.Create(cmd, options);
    }

    private int ExecuteCommandSync(string command)
    {
        var cmdInfo=GetExecuteCommandSync();

        if(IsUnixPlatform(BuildHostPlatform.Current.Platform)) 
        {
            command=" \""+command.Replace("\"", "\\\"")+" \"";
        }

        Console.WriteLine("Calling: "+cmdInfo.Item1+" "+cmdInfo.Item2+command);

        var processInfo = new ProcessStartInfo(cmdInfo.Item1, cmdInfo.Item2+command)
        {
            CreateNoWindow=true,
            UseShellExecute=false,
            RedirectStandardError=true,
            RedirectStandardOutput=true,
            WorkingDirectory=m_modulePath
        };

        StringBuilder outputString = new StringBuilder();
        Process p = Process.Start(processInfo);

        p.OutputDataReceived+=(sender, args) => {outputString.Append(args.Data); Console.WriteLine(args.Data);};
        p.ErrorDataReceived+=(sender, args) => {outputString.Append(args.Data); Console.WriteLine(args.Data);};
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        p.WaitForExit();

        if(p.ExitCode != 0)
        {
             Console.WriteLine(outputString);
        }
        return p.ExitCode;
    }

    private bool IsUnixPlatform(UnrealTargetPlatform platform) {
        return platform == UnrealTargetPlatform.Linux || platform == UnrealTargetPlatform.Mac;
    }
}

public class CMakeTarget : ModuleRules
{
    public CMakeTarget(ReadOnlyTargetRules Target) : base(Target)
	{
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Engine", "InputCore" });
        PrivateDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine"});
    }
    
    public static bool add(ReadOnlyTargetRules target, ModuleRules rules, string targetName, string targetLocation, string args, bool useSystemCompiler=false)
    {
        Console.WriteLine("CMakeTarget load target: "+targetName+" loc:"+targetLocation);
        CMakeTargetInst cmakeTarget = new CMakeTargetInst(targetName, targetLocation, args);

        if(!cmakeTarget.Load(target, rules, useSystemCompiler))
        {
            Console.WriteLine("CMakeTarget failed to load target: "+targetName);
            cmakeTarget.AddFailed(rules);    
            return false;
        }

        if(!cmakeTarget.addRules(rules, useSystemCompiler))
        {
            cmakeTarget.AddFailed(rules);
            Console.WriteLine("CMakeTarget failed to add rules: "+targetName);
            return false;
        }

        return true;
    }
}
