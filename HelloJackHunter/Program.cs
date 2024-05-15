using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace HelloJackHunter
{

    public static class MyExtensions
    {
        public static StringBuilder Prepend(this StringBuilder sb, string content)
        {
            return sb.Insert(0, content + Environment.NewLine);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: HelloJackHunter.exe <path to DLL or directory> <output path>");
                return;
            }

            string inputPath = args[0];
            string outputPath = args[1];

            if (!Directory.Exists(outputPath)) //create output path if doesn't exist
            {
                Directory.CreateDirectory(outputPath);
            }

            if (File.Exists(inputPath))
            {
                // Single file
                CreatePCH(outputPath);
                ProcessDll(inputPath, outputPath);
            }
            else if (Directory.Exists(inputPath))
            {
                // Directory
                CreatePCH(outputPath);
                foreach (string file in Directory.GetFiles(inputPath, "*.dll"))
                {
                    ProcessDll(file, outputPath);
                }
            }
            else
            {
                Console.WriteLine("Invalid path.");
            }
        }

        static void CreatePCH(string outputPath)
        {
            string output_pch = Path.Combine(outputPath, "pch.h");
            string pch_content = @"
#ifndef PCH_H
#define PCH_H

// add headers that you want to pre-compile here
#pragma once
#define UNICODE
#define WIN32_LEAN_AND_MEAN             // Exclude rarely-used stuff from Windows headers
// Windows Header Files
#include <windows.h>

#endif //PCH_H
";
            File.WriteAllText(output_pch, pch_content);
            Console.WriteLine($"Generated {output_pch}");
        }

        static void ProcessDll(string dllPath, string outputPath)
        {
            string outputFileName = Path.Combine(outputPath, Path.GetFileNameWithoutExtension(dllPath) + ".cpp");
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("#include \"pch.h\"");
            sb.AppendLine("#include <windows.h>");
            sb.AppendLine("#include <iostream>");

            try
            {
                string dumpbinOutput = CallDumpbin(dllPath);
                var exportedFunctions = ParseExportedFunctions(dumpbinOutput);

                foreach (string functionName in exportedFunctions)
                {
                    sb.Prepend($"#define {functionName} {functionName}_orig");
                    sb.AppendLine($"#undef {functionName}");
                }


                foreach (string functionName in exportedFunctions)
                {
                    string cppTemplate = GenerateCppTemplate(functionName);
                    sb.AppendLine(cppTemplate);
                }

                sb.AppendLine(GenerateDllMainTemplate());

                File.WriteAllText(outputFileName, sb.ToString());
                Console.WriteLine($"Generated {outputFileName}");

                // Compile the C++ file to DLL
                CompileToDll(outputFileName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {dllPath}: {ex.Message}");
            }
        }

        static string FindVSBinaries(string search)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo()
                {
                    //FileName = "%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe",
                    FileName = "c:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe",
                    Arguments = $"-latest -find {search}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            var Path = process.StandardOutput.ReadToEnd();
            if (process.ExitCode == 0)
            {
                return Path.TrimEnd(); 
            }
            else
            {
                return @"FILE NOT FOUND";
            }
        }

        static string CallDumpbin(string dllPath)
        {
            string dumpbinPath = FindVSBinaries("VC\\Tools\\MSVC\\**\\bin\\Hostx64\\x64\\dumpbin.exe");

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = dumpbinPath,
                Arguments = $"/exports \"{dllPath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(startInfo))
            {
                using (StreamReader reader = process.StandardOutput)
                {
                    return reader.ReadToEnd();
                }
            }
        }

        static HashSet<string> ParseExportedFunctions(string dumpbinOutput)
        {
            HashSet<string> functions = new HashSet<string>();
            string[] lines = dumpbinOutput.Split('\n');
            bool exportsStart = false;

            foreach (string line in lines)
            {
                if (line.Contains("ordinal hint RVA      name"))
                {
                    exportsStart = true;
                    continue;
                }

                if (exportsStart)
                {
                    Match match = Regex.Match(line, @"\s*\d+\s+\d+\s+[A-F0-9]+\s+(\S+)");
                    if (match.Success)
                    {
                        functions.Add(match.Groups[1].Value);
                    }
                }
            }

            return functions;
        }

        static string GenerateCppTemplate(string functionName)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendFormat("extern \"C\" {{\n");
            sb.AppendFormat("    __declspec(dllexport) void {0}() {{\n", functionName);
            sb.AppendFormat("        MessageBox(NULL, L\"ZephrFish DLL Hijack in {0}\", L\"Function Call\", MB_OK);\n", functionName);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        static string GenerateDllMainTemplate()
        {
            return @"
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        MessageBox(NULL, L""ZephrFish DLL Hijack in DLL_PROCESS_ATTACH"", L""DllMain Event"", MB_OK);
        break;
    case DLL_THREAD_ATTACH:
        // Code for thread attachment
        break;
    case DLL_THREAD_DETACH:
        // Code for thread detachment
        break;
    case DLL_PROCESS_DETACH:
        // Code for process detachment
        break;
    }
    return TRUE;
}";
        }


        static string FindCL()
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo()
                {
                    //FileName = "%ProgramFiles(x86)%\\Microsoft Visual Studio\\Installer\\vswhere.exe",
                    FileName = "c:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe",
                    Arguments = "-latest -find VC\\Tools\\MSVC\\**\\bin\\Hostx64\\x64\\cl.exe",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.WaitForExit();
            var Path = process.StandardOutput.ReadToEnd();
            if (process.ExitCode == 0)
            {
                return Path.TrimEnd();
            }
            else
            {
                return @"C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.37.32822\bin\Hostx64\x64\cl.exe";
            }
        }



        static void CompileToDll(string cppFileName)
        {
            string outputDllName = Path.ChangeExtension(cppFileName, ".dll");
            string compilerPath = FindVSBinaries("VC\\Tools\\MSVC\\**\\bin\\Hostx64\\x64\\cl.exe");
            string compilerArgs = $"/DYNAMICBASE \"user32.lib\" /LD {cppFileName} /Fe{outputDllName} /Fo{Path.GetDirectoryName(cppFileName)}\\";
            string DevCMDPath = FindVSBinaries("Common7\\Tools\\VsDevCmd.bat");
            Console.WriteLine(compilerPath + " " + compilerArgs);
            Process compiler = new Process();

            compiler.StartInfo.FileName = "cmd.exe";
            //compiler.StartInfo.WorkingDirectory = tempPath;
            compiler.StartInfo.RedirectStandardInput = true;
            compiler.StartInfo.RedirectStandardOutput = true;
            compiler.StartInfo.UseShellExecute = false;
            try { 
                compiler.Start();
                compiler.StandardInput.WriteLine("\"" + DevCMDPath + "\"" + " -startdir=none -arch=x64 -host_arch=x64 /no_logo");
                compiler.StandardInput.WriteLine($"cl.exe {compilerArgs}");
                compiler.StandardInput.WriteLine(@"exit");
                string output = compiler.StandardOutput.ReadToEnd();
                compiler.WaitForExit();
                if (compiler.ExitCode != 0 ) {
                    Console.Write(output); //Only show cl.exe output if failed
                    compiler.Close();
                    throw new ArgumentException("Non Zero Compilation Exit Code (Find better exception class");
                }
                compiler.Close();
                Console.WriteLine($"Compiled {outputDllName}");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error compiling {cppFileName}: {ex.Message}");
            }

        }

    }
}

