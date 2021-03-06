﻿using System;
using System.Collections.Generic;
using System.IO;

namespace MeadowApiModeSwitcher
{
    class Program
    {
        static FileInfo[] projectFiles;

        //ToDo update to a command line arg
        static string MeadowFoundationPath = "../../../../../Meadow.Foundation";

        static void Main(string[] args)
        {
            Console.WriteLine("Hello Meadow developers!");

            //check if path exists first
            if (Directory.Exists(MeadowFoundationPath))
            {
                projectFiles = GetCsProjFiles(MeadowFoundationPath);
            }

            SwitchToDeveloperMode(projectFiles);

          //  SwitchToPublishingMode(projectFiles);
        }

        static void SwitchToPublishingMode(FileInfo[] files)
        {
            Console.WriteLine("Developer mode");

            foreach (var f in files)
            {
                Console.WriteLine($"Found {f.Name}");

                var referencedProjects = GetListOfProjectReferencesInProject(f);

                foreach (var p in referencedProjects)
                {
                    var refProjFileInfo = GetFileInfoForProjectName(p, files);

                    if (refProjFileInfo == null)
                    {   //referenced project outside of foundation (probably core)
                        continue;
                    }

                    //time to change the file
                    ReplaceLocalRefWithNugetRef(f, p, refProjFileInfo);
                }
            }
        }

        static void SwitchToDeveloperMode(FileInfo[] files)
        {
            foreach (var f in files)
            {
                Console.WriteLine($"Found {f.Name}");
                var packageIds = GetListOfNugetReferencesInProject(f);

                foreach (var id in packageIds)
                {
                    //get the csproj file info that maps to the referenced nuget package
                    var nugetProj = GetFileForPackageId(files, id);

                    if (nugetProj == null)
                    {   //likely means it's referencng and external nuget package
                        continue;
                    }

                    ReplaceNugetRefWithLocalRef(f, id, nugetProj);
                }
            }
        }

        static FileInfo GetFileInfoForProjectName(string projectName, FileInfo[] files)
        {
            foreach (var f in files)
            {
                var name = Path.GetFileName(f.FullName);

                if (Path.GetFileName(f.FullName) == projectName)
                {
                    return f;
                }
            }

            return null;
        }

        static void ReplaceLocalRefWithNugetRef(FileInfo fileInfoToModify, string fileName, FileInfo fileInfoToReference)
        {
            var lines = File.ReadAllLines(fileInfoToModify.FullName);

            var newLines = new List<string>();

            Console.WriteLine($"ReplaceLocalRef: {fileName}");

            foreach (var line in lines)
            {
                //skip the line we're removing
                if (line.Contains("ProjectReference") && line.Contains(fileName))
                {
                    var nugetInfo = GetNugetInfoFromFileInfo(fileInfoToReference);

                    if(nugetInfo == null)   //if it's null it's missing meta daa
                    {                       //which means it's not published
                        newLines.Add(line);
                    }
                    else
                    {
                        Console.WriteLine($"Nuget: {nugetInfo.Item1} Version: {nugetInfo.Item2}");

                        string newLine = $"    <PackageReference Include=\"{nugetInfo.Item1}\" Version=\"{nugetInfo.Item2}\" />";

                        newLines.Add(newLine);
                    }
                }
                else
                {
                    newLines.Add(line);
                }
            }

            File.WriteAllLines(fileInfoToModify.FullName, newLines.ToArray());
        }

        static void ReplaceNugetRefWithLocalRef(FileInfo fileInfoToModify, string packageId, FileInfo fileInfoToReference)
        {
            var lines = File.ReadAllLines(fileInfoToModify.FullName);

            var newLines = new List<string>();

            foreach (var line in lines)
            {
                //skip the line we're removing
                if (line.Contains("PackageReference") && line.Contains(packageId))
                {
                    var path = Path.GetRelativePath(fileInfoToModify.DirectoryName, fileInfoToReference.DirectoryName);

                    path = Path.Combine(path, Path.GetFileName(fileInfoToReference.FullName));

                    string newLine = $"    <ProjectReference Include=\"{path}";

                    newLine = newLine.Replace("/", "\\") + "\" />";
                    newLines.Add(newLine);
                }
                else
                {
                    newLines.Add(line);
                }
            }

            File.WriteAllLines(fileInfoToModify.FullName, newLines.ToArray());
        }

        static FileInfo[] GetCsProjFiles(string path)
        {
            return (new DirectoryInfo(path)).GetFiles("*.csproj", SearchOption.AllDirectories);
        }

        static Tuple<string, string> GetNugetInfoFromFileInfo(FileInfo file)
        {
            var lines = File.ReadAllLines(file.FullName);

            string packageId = string.Empty;
            string version = string.Empty;

            //we'll check for metadata that verifies if it's published
            bool isPublished = false;

            foreach(var line in lines)
            {
                if (line.Contains("PackageId"))
                {
                    var startIndex = line.IndexOf(">") + 1;
                    var endIndex = line.LastIndexOf("<");

                    packageId = line.Substring(startIndex, endIndex - startIndex);
                    isPublished = true;
                }

                if(line.Contains("<Version>"))
                {
                    var startIndex = line.IndexOf(">") + 1;
                    var endIndex = line.LastIndexOf("<");

                    version = line.Substring(startIndex, endIndex - startIndex);
                }
            }

            if(isPublished)
            {
                return new Tuple<string, string>(packageId, version);
            }
            return null;
        }

        static FileInfo GetFileForPackageId(FileInfo[] fileInfos, string packageId)
        {
            foreach (var f in fileInfos)
            {
                using (var sr = f.OpenText())
                {
                    string line;

                    while (true)
                    {
                        line = sr.ReadLine();

                        if (line == null)
                        {
                            break;
                        }

                        if (line.Contains("PackageId") && line.Contains(packageId))
                        {
                            return f;
                        }
                    }
                }
            }

            return null;
        }

        static List<string> GetListOfProjectReferencesInProject(FileInfo fileInfo)
        {
            var projects = new List<string>();

            using (var sr = fileInfo.OpenText())
            {
                string line;

                while (true)
                {
                    line = sr.ReadLine();

                    if (line == null)
                        break;

                    if (line.Contains("ProjectReference"))
                    {
                        int firstQuote = line.LastIndexOf("\\");
                        int secondQuote = line.IndexOf("\"", firstQuote + 1);

                        var projectName = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

                      //  var projectName = Path.GetFileNameWithoutExtension(projPath);

                        Console.WriteLine($"Found project ref: {projectName}");

                        projects.Add(projectName);
                    }
                }
            }

            return projects;
        }

        static List<string> GetListOfNugetReferencesInProject(FileInfo fileInfo)
        {
            var nugets = new List<string>();

            using (var sr = fileInfo.OpenText())
            {
                string line;

                while (true)
                {
                    line = sr.ReadLine();

                    if (line == null)
                        break;

                    if (line.Contains("PackageReference"))
                    {
                        int firstQuote = line.IndexOf("\"");
                        int secondQuote = line.IndexOf("\"", firstQuote + 1);

                        var packageName = line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);

                        Console.WriteLine($"Found package: {packageName}");

                        nugets.Add(packageName);
                    }
                }
            }

            return nugets;
        }
    }
}