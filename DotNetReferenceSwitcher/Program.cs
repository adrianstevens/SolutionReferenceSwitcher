using System;
using System.Collections.Generic;
using System.IO;

namespace MeadowApiModeSwitcher
{
    class Program
    {
        static List<FileInfo> sourceProjectFiles;
        static FileInfo[] updateFiles;

        //ToDo update to a command line arg
        static string[] sourcePaths =
        {
            $"c:/WL/Meadow.Foundation",
            $"c:/WL/Meadow.Foundation.FeatherWings",
            $"c:/WL/Meadow.Foundation.Grove",
            $"c:/WL/Meadow.Foundation.mikroBUS",
            $"c:/WL/Meadow.Core",
            $"c:/WL/Meadow.Logging",
            $"c:/WL/Maple",
        };

        static string updatePath = $"c:/WL/Meadow.Foundation.Grove";

        static string WildcardVersion = "0.*";

        static (string projectName, string nugetName)[] ExternalProjects =
        {
            ("Meadow.F7", "Meadow.F7"),
            ("Meadow.Logging", "Meadow.Logging"),
            ("Meadow.Core", "Meadow")
        };

        static void Main(string[] args)
        {
            Console.WriteLine("Hello Developers!");

            updateFiles = GetCsProjFiles(updatePath);

            sourceProjectFiles = new List<FileInfo>();

            //check if path exists first
            foreach(var path in sourcePaths)
            {
                if (Directory.Exists(path))
                {
                    var projects = GetCsProjFiles(path);

                    sourceProjectFiles.AddRange(projects);
                }
            }

          //  SwitchToDeveloperMode(projectFiles);

            SwitchToPublishingMode(sourceProjectFiles, updateFiles);
        }

        static void SwitchToPublishingMode(List<FileInfo> sourceProjectFiles, FileInfo[] updateProjectFiles)
        {
            Console.WriteLine("Developer mode");

            foreach (var projectFile in updateProjectFiles)
            {
                Console.WriteLine($"Found {projectFile.Name}");

                var referencedProjects = GetListOfProjectReferencesInProject(projectFile);

                foreach (var project in referencedProjects)
                {
                    var refProjFileInfo = GetFileInfoForProjectName(project, sourceProjectFiles);

                    if (refProjFileInfo == null)
                    {   //referenced project outside of foundation (probably core)
                        foreach (var externalProject in ExternalProjects)
                        {
                            ReplaceExternalRefWithNuget(projectFile, 
                                                        externalProject.projectName, 
                                                        externalProject.nugetName, 
                                                        WildcardVersion);
                        }

                        continue;
                    }

                    //time to change the file
                    ReplaceLocalRefWithNuget(projectFile, refProjFileInfo, true);
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

        static FileInfo GetFileInfoForProjectName(string projectName, List<FileInfo> files)
        {
            foreach (var f in files)
            {
                if (Path.GetFileName(f.FullName) == projectName)
                {
                    return f;
                }
            }

            return null;
        }

        static void ReplaceExternalRefWithNuget(FileInfo fileInfoToModify, string projectName, string nugetPackageId, string nugetVersion)
        {
            Console.WriteLine($"ReplaceLocalRef: {projectName}");

            var lines = File.ReadAllLines(fileInfoToModify.FullName);

            var newLines = ReplaceLocalRefsWithNuget(lines,
                                                    projectName,
                                                    nugetPackageId,
                                                    nugetVersion);

            File.WriteAllLines(fileInfoToModify.FullName, newLines.ToArray());
        }

        static List<string> ReplaceLocalRefsWithNuget(string[] lines, string projectName, string packageId, string version = "0.*")
        {
            var newLines = new List<string>();

            foreach (var line in lines)
            {
                //skip the line we're removing
                if (line.Contains("ProjectReference") && line.Contains(projectName))
                {
                    Console.WriteLine($"Nuget: {packageId} Version: {version}");

                    string newLine = $"    <PackageReference Include=\"{packageId}\" Version=\"{version}\" />";

                    newLines.Add(newLine);
                }
                else
                {
                    newLines.Add(line);
                }
            }

            return newLines;
        }

        static void ReplaceLocalRefWithNuget(FileInfo fileInfoToModify, FileInfo fileInfoToReference, bool useWildcard = true)
        {
            var referencedProjectFileName = fileInfoToReference.Name;

            var nugetInfo = GetNugetInfoFromFileInfo(fileInfoToReference);

            if(nugetInfo == null)
            {   //no nuget to replace
                Console.WriteLine($"Could not find nuget info for {referencedProjectFileName}");
                return;
            }

            Console.WriteLine($"ReplaceLocalRef: {referencedProjectFileName}");

            var lines = File.ReadAllLines(fileInfoToModify.FullName);

            var newLines = ReplaceLocalRefsWithNuget(lines, 
                                                    referencedProjectFileName, 
                                                    nugetInfo?.PackageId,
                                                    useWildcard ? WildcardVersion : nugetInfo?.Version);

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

        static (string PackageId, string Version)? GetNugetInfoFromFileInfo(FileInfo file)
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
                return (packageId, version);
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
