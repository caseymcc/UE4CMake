
using UnrealBuildTool;
using System;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;

public static class DateTimeExtensions
{
    public static bool EqualsUpToSeconds(this DateTime dt1, DateTime dt2)
    {
        return dt1.Year == dt2.Year && dt1.Month == dt2.Month && dt1.Day == dt2.Day &&
               dt1.Hour == dt2.Hour && dt1.Minute == dt2.Minute && dt1.Second == dt2.Second;
    }   
}

public class CMakeTargetInst
{
    private string m_cmakeTargetPath;
    private string m_modulePath;
    private string m_targetName;
    private string m_targetLocation;
    private string m_cmakeArgs;
//    private string[] m_includeDirectories;
//    private string[] m_libraries;

    private string m_buildDirectory;
    private string m_buildPath;
    private string m_generatedTargetPath;

    private string m_thirdPartyDir = "../ThirdParty";
    private string m_thirdPartyPath;
    private string m_thirdPartyGeneratedPath;

    private string m_buildInfoFile;
    private string m_buildInfoPath;

//    public static bool add(ReadOnlyTargetRules target, ModuleRules rules, string targetName, string targetLocation, string args)
//    {
//        CMakeTarget cmakeTarget = new CMakeTarget(targetName, targetLocation, args);
//
//        if(!cmakeTarget.load(target, rules))
//            return false;
//
//        cmakeTarget.addRules(rules);
//        return true;
//
//    }

    
    public CMakeTargetInst(string targetName, string targetLocation, string args)
    {
        m_targetName=targetName;
        m_targetLocation=targetLocation;
        m_cmakeArgs=args;
    }

    public void addRules(ModuleRules rules)
    {
        Console.WriteLine("Loading build info file: "+m_buildInfoPath);

        if(!File.Exists(m_buildInfoPath))
        {
            Console.WriteLine("Failed loading: "+m_buildInfoPath);
            return;
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


        if(values.ContainsKey("dependencies"))
        {
            string[] dependencies = values["dependencies"].Split(',');

            foreach(string depend in dependencies)
            {
//                Console.WriteLine("Adding depends: "+depend);
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
                string dependPath=Path.Combine(sourcePath, depend);

//                Console.WriteLine("Adding depends: "+dependPath);
                rules.ExternalDependencies.Add(dependPath);
            }
        }
        if(values.ContainsKey("includes"))
        {
            string[] includes = values["includes"].Split(',');

            foreach(string include in includes)
            {
//                Console.WriteLine("Adding include: "+include);
                rules.PublicIncludePaths.Add(include);
            }
        }

        if(values.ContainsKey("libraries"))
        {
            string[] libraries = values["libraries"].Split(',');

            foreach(string library in libraries)
            {
//                Console.WriteLine("Adding library: "+library);
                rules.PublicAdditionalLibraries.Add(library);
            }
        }
    }

    public bool load(ReadOnlyTargetRules target, ModuleRules rules)
    {
        string buildType = "Release";

        switch(target.Configuration)
        {
            case UnrealTargetConfiguration.Debug:
            case UnrealTargetConfiguration.Development:
            case UnrealTargetConfiguration.DebugGame:
                buildType="Debug";
                break;
            default:
                break;
        }

        m_cmakeTargetPath=Path.GetFullPath(rules.Target.ProjectFile.FullName);
        m_cmakeTargetPath=Directory.GetParent(m_cmakeTargetPath).FullName+"/Plugins/CMakeTarget/Source";
        Console.WriteLine("m_cmakeTargetPath: "+m_cmakeTargetPath);

        m_modulePath=Path.GetFullPath(rules.ModuleDirectory);
        Console.WriteLine("m_modulePath: "+m_modulePath);
        m_thirdPartyPath=Path.Combine(m_modulePath, m_thirdPartyDir);
        m_thirdPartyGeneratedPath=Path.Combine(m_thirdPartyPath, "generated");
        m_generatedTargetPath=Path.Combine(m_thirdPartyGeneratedPath, m_targetName);
        m_buildDirectory="build";
        m_buildPath=Path.Combine(m_generatedTargetPath, m_buildDirectory);

        m_buildInfoFile="buildinfo_"+buildType+".output";
        m_buildInfoPath=Path.Combine(m_buildPath, m_buildInfoFile).Replace("\\", "/");

        if(!Directory.Exists(m_generatedTargetPath))
            Directory.CreateDirectory(m_generatedTargetPath);

        if(!Directory.Exists(m_buildPath))
            Directory.CreateDirectory(m_buildPath);

        var moduleBuilt = build(target, buildType);

        if(!moduleBuilt)
        {
            return false;
        }
        return true;
    }

    private bool build(ReadOnlyTargetRules target, string buildType)
    {
        string builtFile = Path.Combine(m_generatedTargetPath, buildType+".built");
        string projectCMakeLists=Path.GetFullPath(Path.Combine(m_targetLocation, "CMakeLists.txt"));

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

            var configureCommand = CreateCMakeConfigCommand(target, m_buildPath, buildType);
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

    private string GetGeneratorName(WindowsCompiler compiler)
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
        case WindowsCompiler.VisualStudio2017:
            generatorName="Visual Studio 15 2017";
        break;
        case WindowsCompiler.VisualStudio2019:
            generatorName="Visual Studio 16 2019";
        break;
        }

        return generatorName;
    }

    private string GetGeneratorOptions(WindowsCompiler compiler, WindowsArchitecture architecture)
    {
        string generatorOptions="";

        if((compiler == WindowsCompiler.VisualStudio2017) || (compiler == WindowsCompiler.VisualStudio2019))
        {
            if(architecture == WindowsArchitecture.x86)
                generatorOptions="-A Win32";
            else if(architecture == WindowsArchitecture.x64)
                generatorOptions="-A x64";
            else if(architecture == WindowsArchitecture.ARM32)
                generatorOptions="-A ARM";
            else if(architecture == WindowsArchitecture.ARM64)
                generatorOptions="-A ARM64";
        }
        return generatorOptions;
    }

    private string CreateCMakeConfigCommand(ReadOnlyTargetRules target, string buildDirectory, string buildType)
    {
        const string program = "cmake.exe";
        string generator = GetGeneratorName(target.WindowsPlatform.Compiler);//"Visual Studio 16 2019";
        string generatorOptions = GetGeneratorOptions(target.WindowsPlatform.Compiler, target.WindowsPlatform.Architecture);//"-A x64";

        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");
        string toolchainPath = Path.Combine(m_generatedTargetPath, "toolchain.cmake");

        generateCMakeFile(target, cmakeFile, buildType);
        generateToolchain(target, toolchainPath);

        var installPath = Path.Combine(m_thirdPartyPath, "generated");

        var arguments = " -G \""+generator+"\""+
                        " "+generatorOptions+" "+
                        " -S "+m_generatedTargetPath+
                        " -B "+buildDirectory+
                        " -T host=x64"+
                        " -DCMAKE_INSTALL_PREFIX="+installPath+
                        " -DCMAKE_TOOLCHAIN_FILE="+toolchainPath+
                        " "+m_cmakeArgs;

        return program+arguments;
    }

    private bool generateToolchain(ReadOnlyTargetRules target, string path)
    {
        if(target.Platform == UnrealTargetPlatform.Win64)
        {
            generateWindowsToolchain(target, path);
            return true;
        }
        return false;
    }

    private void generateWindowsToolchain(ReadOnlyTargetRules target, string path)
    {
        string templateFilePath = Path.Combine(m_cmakeTargetPath, "toolchains/windows_toolchain.in");
        string contents = File.ReadAllText(templateFilePath);
        bool forceReleaseRuntime=true;

        if((target.Configuration == UnrealTargetConfiguration.Debug) && (target.bDebugBuildsActuallyUseDebugCRT))
            forceReleaseRuntime=false;
        contents=contents.Replace("@FORCE_RELEASE_RUNTIME@", forceReleaseRuntime?"ON":"OFF");

        File.WriteAllText(path, contents);
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
        contents=contents.Replace("@BUILD_TARGET_DIR@", m_targetLocation.Replace("\\", "/"));
        contents=contents.Replace("@BUILD_TARGET_THIRDPARTY_DIR@", m_thirdPartyGeneratedPath.Replace("\\", "/"));
        contents=contents.Replace("@BUILD_TARGET_BUILD_DIR@", buildDir.Replace("\\", "/"));

        File.WriteAllText(cmakeFile, contents);

        return true;
    }

    private string CreateCMakeBuildCommand(string buildDirectory, string buildType)
    {
        return "cmake.exe --build "+buildDirectory+" --config "+buildType;
    }

    private string CreateCMakeInstallCommand(string buildDirectory, string buildType)
    {
        return "cmake.exe --build "+buildDirectory+" --target install --config "+buildType;
    }

    private int ExecuteCommandSync(string command)
    {
        Console.WriteLine("Running: "+command);
        var processInfo = new ProcessStartInfo("cmd.exe", "/c "+command)
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
}

public class CMakeTarget : ModuleRules
{
    public CMakeTarget(ReadOnlyTargetRules Target) : base(Target)
	{
//        Target.PublicIncludePaths.add(Target.ModuleDirectory);
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Engine", "InputCore" });
        PrivateDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine"});
    }
    
    public static bool add(ReadOnlyTargetRules target, ModuleRules rules, string targetName, string targetLocation, string args)
    {
        Console.WriteLine("CMakeTarget load target: "+targetName+" loc:"+targetLocation);
        CMakeTargetInst cmakeTarget = new CMakeTargetInst(targetName, targetLocation, args);

        if(!cmakeTarget.load(target, rules))
        {
            Console.WriteLine("CMakeTarget failed to load target: "+targetName);
            return false;
        }

        cmakeTarget.addRules(rules);
        return true;
    }
}