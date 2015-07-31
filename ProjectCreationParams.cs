using System.Collections.Generic;
using BigCity.ProjectModel;
using RestSharp;

namespace BigCity
{
  public class ProjectCreationParams
  {
    public string ServerUrl;
    public string Username;
    public string Password;
    public string ProjectName;
    public string SolutionPath;
    public string SolutionRelativePath;

    public ILog Log;

    public RestClient Client;
    public ProjectCreator.Project Project;
    public List<HashSet<ProjectData>> Layers;
  }
}