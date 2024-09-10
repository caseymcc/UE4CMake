using UnrealBuildTool;
using UnrealBuildBase;
using EpicGames.Core;
using System;
using System.Reflection;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

public static class DateTimeExtensions
{
    public static bool EqualsUpToSeconds(this DateTime dt1, DateTime dt2)
    {
        return dt1.Year == dt2.Year && dt1.Month == dt2.Month && dt1.Day == dt2.Day &&
               dt1.Hour == dt2.Hour && dt1.Minute == dt2.Minute && dt1.Second == dt2.Second;
    }   
}

public class LocalLogger : ILogger
{
    public IDisposable BeginScope<TState>(TState state) => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        Console.WriteLine(formatter(state, exception));
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

        Console.WriteLine("******************************************");
        Console.WriteLine("values: "+values);

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
                    rules.PublicSystemLibraries.Add("stdc++"); //only include if using included compiler and linux  
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
                buildType="Debug";
                break;
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

//        if(configCMake)
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
#if !UE_5_4_OR_LATER
        case WindowsCompiler.VisualStudio2019:
            generatorName="Visual Studio 16 2019";
        break;
#endif//!UE_5_4_OR_LATER
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

        if((compiler == WindowsCompiler.VisualStudio2022)
#if !UE_5_4_OR_LATER
            || (compiler == WindowsCompiler.VisualStudio2019)
#endif//!UE_5_4_OR_LATER 
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

    private string getGeneratorOptions(ReadOnlyTargetRules target, ModuleRules rules)
    {
        string options = "";

        try{
            Type type_UEBuildPlatform = Utils.getBuildToolType("UEBuildPlatform");
            Type type_UEBuildTarget = Utils.getBuildToolType("UEBuildTarget");
            Type type_UEBuildModule = Utils.getBuildToolType("UEBuildModule");
            Type type_UEBuildModuleCPP = Utils.getBuildToolType("UEBuildModuleCPP");
  //          Type type_TargetDescriptor = Utils.getBuildToolType("TargetDescriptor");
  //          Type type_BuildConfiguration = Utils.getBuildToolType("BuildConfiguration");
            Type type_UEToolChain = Utils.getBuildToolType("UEToolChain");

            MethodInfo method_GetBuildPlatform = Utils.getMethod(type_UEBuildPlatform, "GetBuildPlatform", BindingFlags.Public | BindingFlags.Static);
//            MethodInfo method_BuildTarget_Create = Utils.getMethod(type_UEBuildTarget, "Create", BindingFlags.Public | BindingFlags.Static, new Type[] { type_TargetDescriptor, typeof(ILogger)});
            MethodInfo method_GetCppConfiguration = Utils.getMethod(type_UEBuildTarget, "GetCppConfiguration", BindingFlags.Public | BindingFlags.Static);
            MethodInfo method_SetupGlobalEnvironment = Utils.getMethod(type_UEBuildTarget, "SetupGlobalEnvironment", BindingFlags.Public | BindingFlags.Instance);

            object buildPlatform = null;// = Activator.CreateInstance(type_UEBuildPlatform);
            buildPlatform = method_GetBuildPlatform.Invoke(null, new object[] { target.Platform });

//            method_BuildTarget_Create.Invoke(null, new object[] { target, null });
            
            Console.WriteLine("buildPlatform: " + buildPlatform);
            if (buildPlatform == null)
            {
                Console.WriteLine("buildPlatform is null");
                return options;
            }

            Console.WriteLine("BuildPlatform Type: " + buildPlatform.GetType().Name);

            Type type_buildPlatform = buildPlatform.GetType();
            MethodInfo method_CreateToolChain = Utils.getMethod(type_buildPlatform, "CreateToolChain", BindingFlags.Instance | BindingFlags.Public);

            object toolChain = method_CreateToolChain.Invoke(buildPlatform, new object[] { target });

            if(toolChain == null)
            {
                Console.WriteLine("toolChain is null");
                return options;
            }
            Console.WriteLine("ToolChain Type: " + toolChain.GetType().Name);

            Type type_ToolChain = toolChain.GetType();
            Type type_CppConfiguration = Utils.getBuildToolType("CppConfiguration");

            object cppConfiguration = method_GetCppConfiguration.Invoke(null, new object[] { target.Configuration });
            
            if(cppConfiguration == null)
            {
                Console.WriteLine("cppConfiguration is null");
                return options;
            }

            Type type_CppCompileEnvironment = Utils.getBuildToolType("CppCompileEnvironment");
            Type type_LinkEnvironment = Utils.getBuildToolType("LinkEnvironment");
            Type type_SourceFileMetadataCache = Utils.getBuildToolType("SourceFileMetadataCache");

            ConstructorInfo ctor_CppCompileEnvironment = Utils.getConstructor(type_CppCompileEnvironment, new Type[] { typeof(UnrealTargetPlatform), type_CppConfiguration, typeof(UnrealArchitectures), type_SourceFileMetadataCache });

            object cppCompileEnvironment = ctor_CppCompileEnvironment.Invoke(new object[] { target.Platform, cppConfiguration, target.Architectures, null });

            if(cppCompileEnvironment == null)
            {
                Console.WriteLine("cppCompileEnvironment is null");
                return options;
            }

            List<string> arguments = new List<string>();

            MethodInfo method_GetCompileArguments_Global = Utils.getMethod(type_ToolChain, "GetCompileArguments_Global", BindingFlags.Instance | BindingFlags.NonPublic);

//            method_GetCompileArguments_Global.Invoke(toolChain, new object[] { cppCompileEnvironment, arguments });

            ConstructorInfo ctor_UEBuildModuleCPP=Utils.getConstructor(type_UEBuildModuleCPP, new Type[] { typeof(ModuleRules), typeof(DirectoryReference), typeof(DirectoryReference), typeof(DirectoryReference), typeof(ILogger) });
//            ConstructorInfo ctor_UEBuildModuleCPP=Utils.getConstructor(type_UEBuildModuleCPP);

            PropertyInfo property_Directory = Utils.getProperty(rules.GetType(), "Directory", BindingFlags.Instance | BindingFlags.NonPublic);
            DirectoryReference directory = (DirectoryReference)property_Directory.GetValue(rules);

            if(directory == null)
            {
                Console.WriteLine("directory is null");
                return options;
            }

            FieldInfo field_PrivateDependencyModules = Utils.getField(type_UEBuildModuleCPP, "PrivateDependencyModules");
            FieldInfo field_PrivateIncludePathModules = Utils.getField(type_UEBuildModuleCPP, "PrivateIncludePathModules");
            FieldInfo field_PublicDependencyModules = Utils.getField(type_UEBuildModuleCPP, "PublicDependencyModules");
            FieldInfo field_PublicIncludePathModules = Utils.getField(type_UEBuildModuleCPP, "PublicIncludePathModules");

            object ueBuildModuleCpp=ctor_UEBuildModuleCPP.Invoke(new object[] { rules, directory, 
                directory, directory, rules.Logger });

            if(ueBuildModuleCpp == null)
            {
                Console.WriteLine("ueBuildModuleCpp is null");
                return options;
            }

            Type listOfUEBuildModule = typeof(List<>).MakeGenericType(type_UEBuildModule);

            object privateDependencyModules = Activator.CreateInstance(listOfUEBuildModule);
            object privateIncludePathModules = Activator.CreateInstance(listOfUEBuildModule);
            object publicDependencyModules = Activator.CreateInstance(listOfUEBuildModule);
            object publicIncludePathModules = Activator.CreateInstance(listOfUEBuildModule);

            field_PrivateDependencyModules.SetValue(ueBuildModuleCpp, privateDependencyModules);
            field_PrivateIncludePathModules.SetValue(ueBuildModuleCpp, privateIncludePathModules);
            field_PublicDependencyModules.SetValue(ueBuildModuleCpp, publicDependencyModules);
            field_PublicIncludePathModules.SetValue(ueBuildModuleCpp, publicIncludePathModules);

            MethodInfo method_CreateModuleCompileEnvironment = Utils.getMethod(type_UEBuildModuleCPP, "CreateModuleCompileEnvironment", BindingFlags.Instance | BindingFlags.Public);

            Utils.PrintObject(rules);
//            Utils.PrintMethodInfo(method_CreateModuleCompileEnvironment);
            Utils.PrintObject(cppCompileEnvironment);
            Console.WriteLine("Testing: 3");
            LocalLogger localLogger = new LocalLogger();
            object moduleCompileEnvironment = method_CreateModuleCompileEnvironment.Invoke(ueBuildModuleCpp, new object[] { target, cppCompileEnvironment, rules.Logger });
//            object moduleCompileEnvironment;
//            method_CreateModuleCompileEnvironment.Invoke(ueBuildModuleCpp, new object[] { target, cppCompileEnvironment, localLogger});//rules.Logger });
            if(moduleCompileEnvironment == null)
            {
                Console.WriteLine("moduleCompileEnvironment is null");
                return options;
            }

            Utils.PrintObject(moduleCompileEnvironment);
            Console.WriteLine("moduleCompileEnvironment Type: " + moduleCompileEnvironment.GetType().Name);
            method_GetCompileArguments_Global.Invoke(toolChain, new object[] { moduleCompileEnvironment, arguments });

            Type type_ClangToolChain = Utils.getBuildToolType("ClangToolChain");

            if (type_ClangToolChain.IsAssignableFrom(toolChain.GetType()))
            {
                MethodInfo method_AddExtraToolArguments = Utils.getMethod(type_ClangToolChain, "AddExtraToolArguments", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo method_GetCppStandardCompileArgument = Utils.getMethod(type_ClangToolChain, "GetCppStandardCompileArgument", BindingFlags.Instance | BindingFlags.NonPublic);

                method_AddExtraToolArguments.Invoke(toolChain, new object[] { arguments });
                method_GetCppStandardCompileArgument.Invoke(toolChain, new object[] { moduleCompileEnvironment, arguments });
            }
//            ctor_UEBuildModuleCPP.Invoke(new object[] { rules, GetModuleIntermediateDirectory(RulesObject, Architectures), 
//                GetModuleIntermediateDirectory(RulesObject, null), rules.Context.DefaultOutputBaseDir, rules.Logger });

//            Type type_LinuxToolChain = Utils.getBuildToolType("LinuxToolChain");
//            return new UEBuildModuleCPP(
//							Rules: RulesObject,
//							IntermediateDirectory: GetModuleIntermediateDirectory(RulesObject, Architectures),
//							IntermediateDirectoryNoArch: GetModuleIntermediateDirectory(RulesObject, null),
//							GeneratedCodeDirectory: RulesObject.Context.DefaultOutputBaseDir,
//							Logger
//						);

//            HashSet<DirectoryReference> includePaths = (HashSet<DirectoryReference>)cppCompileEnvironment.GetType().GetField("ModuleInterfacePaths").GetValue(cppCompileEnvironment);
//
//            Console.WriteLine("includePaths: " + includePaths);
//            foreach(DirectoryReference includePath in includePaths)
//            {
//                Console.WriteLine("include: " + includePath);
//                arguments.Add("-I"+includePath);
//            }

            for (int i = 0; i < arguments.Count; i++)
            {
                string pattern = @"-isystem\s*""([^""]+)""";
                arguments[i] = System.Text.RegularExpressions.Regex.Replace(arguments[i], pattern, m =>
                {
                    string path = m.Groups[1].Value;
                    if (!Path.IsPathRooted(path))
                    {
                        path = $"{Unreal.EngineSourceDirectory}/{path}";
                    }
                    return $"-isystem\"{path}\"";
                });

                string sys_pattern = @"--sysroot=\s*""([^""]+)""";
                arguments[i] = System.Text.RegularExpressions.Regex.Replace(arguments[i], sys_pattern, m =>
                {
                    return $"--sysroot=\"{Unreal.EngineSourceDirectory}/{m.Groups[1].Value}\"";
                });

                Console.WriteLine("argument: " + arguments[i]);

            }

            if(arguments.Count > 0)
            {
//                var escapedArguments = arguments.Select(s => Utils.escapeForCMake(s));
                arguments = arguments.Where(s => !s.Contains("(")).ToList();
                options = "-DCMAKE_CXX_FLAGS=\""+string.Join(" ", arguments)+"\" ";
            }
        
        }
        catch(Exception e)
        {
            Utils.PrintExceptionDetails(e);
//            Console.WriteLine("Exception: "+e.Message);
        }

        return options;
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
                }
            }
        }
        else
        {
            name="";
            options="";
        }
        options+=getGeneratorOptions(target, rules);

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

    private void getToolchain(ReadOnlyTargetRules target)
    {
        var platformField = typeof(ReadOnlyTargetRules).GetField("Platform", BindingFlags.Instance | BindingFlags.NonPublic);
        var platform = platformField.GetValue(target);
        var toolchainField = platform.GetType().GetField("ToolChain", BindingFlags.Instance | BindingFlags.NonPublic);
        var toolchain = toolchainField.GetValue(platform);
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

        Console.WriteLine("target.Platform Type: " + target.Platform.GetType().Name);


        var cmakeOptions = new StringBuilder();

//        if (toolchain is LinuxToolChain linuxToolChain)
//        {
//            // Use reflection to get private members of LinuxToolChain
//            var flags = GetPrivateFieldValue<string>(linuxToolChain, "CompileFlags");
//            var defines = GetPrivateFieldValue<List<string>>(linuxToolChain, "CompileDefines");
//
//            cmakeOptions.AppendLine("set(CMAKE_C_FLAGS \"${CMAKE_C_FLAGS} " + flags + "\")");
//            cmakeOptions.AppendLine("set(CMAKE_CXX_FLAGS \"${CMAKE_CXX_FLAGS} " + flags + "\")");
//
//            foreach (var define in defines)
//            {
//                cmakeOptions.AppendLine($"add_definitions(-D{define})");
//            }
//        }
//        else if (toolchain is ClangToolChain clangToolChain)
//        {
//            // Use reflection to get private members of ClangToolChain
//            var flags = GetPrivateFieldValue<string>(clangToolChain, "CompileFlags");
//            var defines = GetPrivateFieldValue<List<string>>(clangToolChain, "CompileDefines");
//
//            cmakeOptions.AppendLine("set(CMAKE_C_FLAGS \"${CMAKE_C_FLAGS} " + flags + "\")");
//            cmakeOptions.AppendLine("set(CMAKE_CXX_FLAGS \"${CMAKE_CXX_FLAGS} " + flags + "\")");
//
//            foreach (var define in defines)
//            {
//                cmakeOptions.AppendLine($"add_definitions(-D{define})");
//            }
//        }

        // Add the generated CMake options to the content
        contents += "\n# Generated CMake options\n" + cmakeOptions.ToString();

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

    // Helper method to get private field value using reflection
    private static T GetPrivateFieldValue<T>(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return (T)field.GetValue(obj);
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

class Utils
{
    public static Type getBuildToolType(string typeName)
    {
        Type type = Type.GetType("UnrealBuildTool."+typeName+", UnrealBuildTool");

        if(type == null)
        {
            throw new Exception("Type not found: "+typeName);
        }

        return type;
    }

    public static ConstructorInfo getConstructor(Type type, Type[] types)
    {
        ConstructorInfo ctor=type.GetConstructor(types);

        if(ctor == null)
        {
            throw new Exception("Constructor not found: "+type.Name);
        }

        return ctor;
    }
    
    public static MethodInfo getMethod(Type type, string methodName, BindingFlags flags)
    {
        MethodInfo method=type.GetMethod(methodName, flags);

        if(method == null)
        {
            throw new Exception("Method not found: "+type.Name+"."+methodName);
        }

        return method;
    }

    public static MethodInfo getMethod(Type type, string methodName, BindingFlags flags, Type[] types  = null)
    {
        MethodInfo method;

        if (types == null || types.Length == 0)
        {
            method=type.GetMethod(methodName, flags);
        }
        else
        {
            method=type.GetMethod(methodName, flags, null, types, null);
        }

        if(method == null)
        {
            throw new Exception("Method not found: "+type.Name+"."+methodName);
        }

        return method;
    }

    public static FieldInfo getField(Type type, string fieldName, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public)
    {
        FieldInfo field=type.GetField(fieldName, flags);

        if(field == null)
        {
            throw new Exception("Field not found: "+type.Name+"."+fieldName);
        }

        return field;
    }

    public static PropertyInfo getProperty(Type type, string propertyName, BindingFlags flags = BindingFlags.Instance | BindingFlags.Public)
    {
        PropertyInfo property=type.GetProperty(propertyName, flags);

        if(property == null)
        {
            throw new Exception("Property not found: "+type.Name+"."+propertyName);
        }

        return property;
    }

    public static string escapeForCMake(string input)
    {
        // Escape backslashes and double quotes
        string escaped = input.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // If the string contains spaces or other special characters, enclose it in quotes
        if (escaped.Contains(" ") || escaped.Contains("\"") || escaped.Contains("\\"))
        {
            return "\"" + escaped + "\"";
        }

        return escaped;
    }

    public static void PrintExceptionDetails(Exception ex)
    {
        Console.WriteLine("Exception Type: " + ex.GetType().FullName);
        Console.WriteLine("Message: " + ex.Message);
        Console.WriteLine("Source: " + ex.Source);
        Console.WriteLine("Stack Trace: " + ex.StackTrace);

        // If there is an inner exception, print details of that as well
        if (ex.InnerException != null)
        {
            Console.WriteLine("Inner Exception:");
            PrintExceptionDetails(ex.InnerException);
        }
    }

    public static void PrintMethodInfo(MethodInfo methodInfo)
    {
        if (methodInfo == null)
        {
            Console.WriteLine("MethodInfo is null");
            return;
        }

        Console.WriteLine("Method Name: " + methodInfo.Name);
        Console.WriteLine("Return Type: " + methodInfo.ReturnType);
        Console.WriteLine("Declaring Type: " + methodInfo.DeclaringType);

        ParameterInfo[] parameters = methodInfo.GetParameters();
        Console.WriteLine("Parameters:");
        foreach (ParameterInfo param in parameters)
        {
            Console.WriteLine(" - " + param.Name + " : " + param.ParameterType);
        }
    }
    public static void PrintObject(object printObject)
    {
        Type type = printObject.GetType();

        Console.WriteLine("****************************************************************");
        Console.WriteLine("Class: " + type.FullName);

        // Output public and non-public properties
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Console.WriteLine("Properties:");
        foreach (var property in properties)
        {
            try
            {
                var value = property.GetValue(printObject);
                Console.WriteLine($"{property.Name}: {value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{property.Name}: Unable to retrieve value ({ex.Message})");
            }
        }

        // Output public and non-public fields
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Console.WriteLine("Fields:");
        foreach (var field in fields)
        {
            try
            {
                var value = field.GetValue(printObject);
                Console.WriteLine($"{field.Name}: {value}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{field.Name}: Unable to retrieve value ({ex.Message})");
            }
        }
    }
}
