using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Build.Execution;

namespace BigCity.ProjectModel
{
  public class Solution
  {
    private readonly string pathToSolutionFile;

    public Solution(string pathToSolutionFile)
    {
      if (string.IsNullOrWhiteSpace(pathToSolutionFile))
        throw new ArgumentNullException("pathToSolutionFile cannot be null or empty");

      if (!File.Exists(pathToSolutionFile))
        throw new FileNotFoundException(string.Format("{0} does not exist", pathToSolutionFile), pathToSolutionFile);

      this.pathToSolutionFile = pathToSolutionFile;
    }

    public List<string> GetProjectFilePaths()
    {
      return ExtractProjects(pathToSolutionFile);
    }

    public IEnumerable<VSProject> GetProjects()
    {
      List<string> projFilePaths = ExtractProjects(pathToSolutionFile);
      var projectItems = new ConcurrentBag<VSProject>();

      // let's do sequential for now
      //foreach (var pf in projFilePaths)
      Parallel.ForEach(projFilePaths, pf =>
      {
        if (File.Exists(pf))
        {
          var projInst = new ProjectInstance(pf);
          var pi = projInst.ToProjectItem();
          projectItems.Add(pi);
        }
      });

      return projectItems;
    }

    public static string GetAbsoluteLocation(string solutionPath, string projectRelativePath)
    {
      string solPath = Path.GetDirectoryName(solutionPath);
      return Path.GetFullPath(Path.Combine(solPath, projectRelativePath));
    }

    private List<string> ExtractProjects(string solutionPath)
    {
      const string matchProjectNameRegex = "^Project\\(\"(?<PROJECTTYPEGUID>.*)\"\\)\\s*=\\s*\"(?<PROJECTNAME>.*)\"\\s*,\\s*\"(?<PROJECTRELATIVEPATH>.*)\"\\s*,\\s*\"(?<PROJECTGUID>.*)\"$";

      using (StreamReader sr = File.OpenText(solutionPath))
      {
        var listOfProjects = new List<string>();

        string lineText;
        while ((lineText = sr.ReadLine()) != null)
        {
          if (lineText.StartsWith("Project("))
          {
            Match projectNameMatch = Regex.Match(lineText, matchProjectNameRegex);
            if (projectNameMatch.Success)
            {
              listOfProjects.Add(GetAbsoluteLocation(solutionPath, projectNameMatch.Groups["PROJECTRELATIVEPATH"].Value));
            }
          }
        }
        sr.Close();
        return listOfProjects;
      }
    }
  }

}