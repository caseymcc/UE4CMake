
using UnrealBuildTool;
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

    public CMakeTargetInst(string targetName, string targetLocation, string args)
    {
        m_targetName=targetName;
        m_targetLocation=targetLocation;
        m_cmakeArgs=args;

        Regex build_type=new Regex(@"-DCMAKE_BUILD_TYPE=(\w*)");
    
        Match match=build_type.Match(args);

        if(match.Success && (match.Groups.Count > 1))
        {
            m_forceBuild=true;
            m_forceBuildType=match.Groups[1].Value;
        }
    }

    public bool addRules(ModuleRules rules)
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
            case UnrealTargetConfiguration.Development:
            case UnrealTargetConfiguration.DebugGame:
                buildType="Debug";
                break;
            default:
                break;
        }

        return buildType;
    }

    public bool Load(ReadOnlyTargetRules target, ModuleRules rules)
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

        var moduleBuilt = Build(target, buildType);

        if(!moduleBuilt)
        {
            return false;
        }
        return true;
    }

    private bool Build(ReadOnlyTargetRules target, string buildType)
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

    private string GetWindowsGeneratorOptions(WindowsCompiler compiler, WindowsArchitecture architecture)
    {
        string generatorOptions="";

        if((compiler == WindowsCompiler.VisualStudio2022) || (compiler == WindowsCompiler.VisualStudio2019)
#if !UE_5_0_OR_LATER
            || (compiler == WindowsCompiler.VisualStudio2017)
#endif//!UE_5_0_OR_LATER 
        )
        {
            if(architecture == WindowsArchitecture.x64)
                generatorOptions="-A x64";
            else if(architecture == WindowsArchitecture.ARM64)
                generatorOptions="-A ARM64";
#if !UE_5_0_OR_LATER
            else if(architecture == WindowsArchitecture.x86)
                generatorOptions="-A Win32";
            else if(architecture == WindowsArchitecture.ARM32)
                generatorOptions="-A ARM";
#endif//!UE_5_0_OR_LATER
        }
        return generatorOptions;
    }

    Tuple<string, string> GetGeneratorInfo(ReadOnlyTargetRules target)
    {
        string name;
        string options;

        if((target.Platform == UnrealTargetPlatform.Win64) 
#if !UE_5_0_OR_LATER
            || (target.Platform == UnrealTargetPlatform.Win32)
#endif//!UE_5_0_OR_LATER
            )
        {
            name=GetWindowsGeneratorName(target.WindowsPlatform.Compiler);
            options=GetWindowsGeneratorOptions(target.WindowsPlatform.Compiler, target.WindowsPlatform.Architecture);
        }
        else if(target.Platform == UnrealTargetPlatform.Linux)
        {
            name="Unix Makefiles";
            options="";
        }
        else
        {
            name="";
            options="";
        }

        return Tuple.Create(name, options);
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

    private string CreateCMakeConfigCommand(ReadOnlyTargetRules target, string buildDirectory, string buildType)
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
        

        var generatorInfo=GetGeneratorInfo(target);

        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");
        string toolchainPath = Path.Combine(m_generatedTargetPath, "toolchain.cmake");

        generateCMakeFile(target, cmakeFile, buildType);
        
        string toolChain="";
        if(generateToolchain(target, toolchainPath))
        {
            toolChain=" -DCMAKE_TOOLCHAIN_FILE=\""+toolchainPath+"\"";
        }

        var installPath = m_thirdPartyGeneratedPath;

        var arguments = " -G \""+generatorInfo.Item1+"\""+
                        " "+generatorInfo.Item2+" "+
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

        File.WriteAllText(path, contents);
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
        else if(BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Linux) 
        {
            cmd="bash";
            options="-c ";
        }
        return Tuple.Create(cmd, options);
    }

    private int ExecuteCommandSync(string command)
    {
        var cmdInfo=GetExecuteCommandSync();

        if(BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Linux) 
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
}

public class CMakeTarget : ModuleRules
{
    public CMakeTarget(ReadOnlyTargetRules Target) : base(Target)
	{
        PublicDependencyModuleNames.AddRange(new string[] { "Core", "Engine", "InputCore" });
        PrivateDependencyModuleNames.AddRange(new string[] { "CoreUObject", "Engine"});
    }
    
    public static bool add(ReadOnlyTargetRules target, ModuleRules rules, string targetName, string targetLocation, string args)
    {
        Console.WriteLine("CMakeTarget load target: "+targetName+" loc:"+targetLocation);
        CMakeTargetInst cmakeTarget = new CMakeTargetInst(targetName, targetLocation, args);

        if(!cmakeTarget.Load(target, rules))
        {
            Console.WriteLine("CMakeTarget failed to load target: "+targetName);
            cmakeTarget.AddFailed(rules);    
            return false;
        }

        if(!cmakeTarget.addRules(rules))
        {
            cmakeTarget.AddFailed(rules);
            Console.WriteLine("CMakeTarget failed to add rules: "+targetName);
            return false;
        }

        return true;
    }
}