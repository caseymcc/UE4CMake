
using UnrealBuildTool;
using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;

public class CMakeTarget
{
    private string m_modulePath;
    private string m_targetName;
    private string m_targetLocation;
    private string[] m_includeDirectories;
    private string[] m_libraries;

    private string m_buildDirectory;
    private string m_buildPath;
    private string m_generatedTargetPath;

    private string m_thirdPartyDir = "../ThirdParty";
    private string m_thirdPartyPath;
    private string m_thirdPartyGeneratedPath;

    private string m_buildInfoFile;
    private string m_buildInfoPath;

    public static bool add(ReadOnlyTargetRules target, ModuleRules rules, string targetName, string targetLocation)
    {
        CMakeTarget cmakeTarget = new CMakeTarget(targetName, targetLocation);

        if(!cmakeTarget.load(target, rules))
            return false;

        cmakeTarget.addRules(rules);
        return true;

    }

    public CMakeTarget(string targetName, string targetLocation)
    {
        m_targetName=targetName;
        m_targetLocation=targetLocation;
    }

    private void addRules(ModuleRules rules)
    {
        if(!File.Exists(m_buildInfoPath))
            return;

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

        if(values.ContainsKey("includes"))
        {
            string[] includes=values["includes"].Split(',');

            foreach(string include in includes)
            {
                Console.WriteLine("Adding include: "+include);
                rules.PublicIncludePaths.Add(include);
            }
        }

        if(values.ContainsKey("libraries"))
        {
            string[] libraries=values["libraries"].Split(',');

            foreach(string library in libraries)
            {
                Console.WriteLine("Adding library: "+library);
                rules.PublicAdditionalLibraries.Add(library);
            }
        }
    }

    private bool load(ReadOnlyTargetRules target, ModuleRules rules)
    {
        if(target.Platform!=UnrealTargetPlatform.Win64)
        {
            return false;
        }

        string buildType = "Debug";

        if(target.Configuration==UnrealTargetConfiguration.Shipping)
        {
            buildType="Release";
        }

        m_modulePath=Path.GetFullPath(rules.ModuleDirectory);
        m_thirdPartyPath=Path.Combine(m_modulePath, m_thirdPartyDir);
        m_thirdPartyGeneratedPath=Path.Combine(m_thirdPartyPath, "generated");
        m_generatedTargetPath=Path.Combine(m_thirdPartyGeneratedPath, m_targetName);
        m_buildDirectory="build";// _" + buildType;
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
        var configureCommand = CreateCMakeConfiCommand(m_buildPath, buildType);
        var configureCode = ExecuteCommandSync(configureCommand);

        if(configureCode!=0)
        {
            Console.WriteLine("Cannot configure CMake project. Exited with code: "
                +configureCode);
            return false;
        }

        var buildCommand = CreateCMakeBuildCommand(m_buildPath, buildType);
        var buildCode = ExecuteCommandSync(buildCommand);
        if(buildCode!=0)
        {
            Console.WriteLine("Cannot build project. Exited with code: "+buildCode);
            return false;
        }
        return true;
    }


    private string CreateCMakeConfiCommand(string buildDirectory, string buildType)
    {
        const string program = "cmake.exe";
        const string generator = "Visual Studio 16 2019";
        const string generatorOptions = "-A x64";

        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");

        if(!File.Exists(cmakeFile))
            generateCMakeFile(cmakeFile, buildType);

        var installPath = Path.Combine(m_thirdPartyPath, "generated");

        var arguments = " -G \""+generator+"\""+
                        " -S "+m_generatedTargetPath+
                        " -B "+buildDirectory+
                        " "+generatorOptions+" "+
                        " -T host=x64"+
                        " -DCMAKE_INSTALL_PREFIX="+installPath;

        return program+arguments;
    }

    private bool generateCMakeFile(string path, string buildType)
    {
        string generateFilePath = Path.Combine(m_modulePath, "CMakeLists.txt");
        string cmakeFile = Path.Combine(m_generatedTargetPath, "CMakeLists.txt");
        const string buildDir = "build";//just one for visual studio generator

        string contents = File.ReadAllText(generateFilePath);

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

        StringBuilder sb = new StringBuilder();
        Process p = Process.Start(processInfo);
        p.OutputDataReceived+=(sender, args) => Console.WriteLine(args.Data);
        p.BeginOutputReadLine();
        p.WaitForExit();

        return p.ExitCode;
    }
}
