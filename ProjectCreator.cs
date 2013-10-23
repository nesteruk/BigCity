using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BigCity.ProjectModel;
using RestSharp;

namespace BigCity
{
  public static class ProjectCreator
  {
    private const string projectResource = "/projects";

    public static void Start(ProjectCreationParams pcp)
    {
      CheckDuplicates(pcp);
    }

    public static void CheckDuplicates(ProjectCreationParams pcp)
    {
      string restRoot = pcp.ServerUrl + (pcp.ServerUrl.EndsWith("/") ? String.Empty : "/") + "httpAuth/app/rest/";

      // check for duplicates
      pcp.Client = new RestClient(restRoot);
      pcp.Client.Authenticator = new HttpBasicAuthenticator(pcp.Username, pcp.Password);
      var rq = new RestRequest(projectResource, Method.GET);
      var resp = pcp.Client.Get<Projects>(rq);
      if (resp.Data == null)
        throw new Exception("Failed to connect. Check server URL, username and password.");

      var ps = resp.Data.project;
      foreach (var serverProject in ps)
      {
        if (serverProject.name.Equals(pcp.ProjectName))
        {
          #if DEBUG
          var drq = new RestRequest(projectResource + "/" + pcp.ProjectName, Method.DELETE);
          pcp.Client.Execute(drq);
          #else
          throw new Exception("A project with this name already exists on the server.");
          #endif
        }
      }

      CreateProject(pcp);
    }

    private static void CreateProject(ProjectCreationParams pcp)
    {
      var rq = new RestRequest(projectResource);
      rq.AddParameter("text/plain", pcp.ProjectName, ParameterType.RequestBody);
      var resp = pcp.Client.Post<Project>(rq);
      pcp.Project = resp.Data;
      if (pcp.Project == null)
        throw new Exception("Failed to create project. Check the user has appropriate rights.");

      BuildSolutionGraph(pcp);
    }

    /// <summary>
    /// Very simple process for a dependency graph. First of all, we check that the bear hasn't
    /// gotten drunk on vodka and isn't trying to reassemble the nuclear generator. Then, we check
    /// the projects to make sure that the first layer contains only the 
    /// </summary>
    /// <param name="client"></param>
    /// <param name="project"></param>
    /// <param name="solutionPath"></param>
    private static void BuildSolutionGraph(ProjectCreationParams pcp)
    {
      List<VSProject> projects;
      var items = GetProjectItemsFromSolution(pcp.SolutionPath, out projects);
      var projectData = items.Values.ToList();
      
      // let's build execution layers here and now
      var layers = new List<HashSet<ProjectData>>();
      int currentLayer = 0;
      do
      {
        var thisLayer = new HashSet<ProjectData>();
        if (currentLayer == 0)
        {
          foreach (var p in projectData)
            if (!p.Dependencies.Any())
              thisLayer.Add(p);
          foreach (var p in thisLayer)
            projectData.Remove(p);
        }
        else
        {
          // all elements dependent on previous layer
          var existingIdx = layers.SelectMany(x => x).Select(y => y.Id).ToSortedSet();
          foreach (var p in projectData)
            if (p.Dependencies.All(d => existingIdx.Contains(d)))
              thisLayer.Add(p);
          foreach (var p in thisLayer)
            projectData.Remove(p);

          if (thisLayer.Count == 0)
          {
            throw new Exception("Failed to find elements for layer " + currentLayer);
          }
        }
        layers.Add(thisLayer);
        currentLayer++;
      } while (projectData.Count > 0);

    build:
      pcp.Layers = layers;
      CreateBuildConfigurations(pcp);
    }

    private static void CreateBuildConfigurations(ProjectCreationParams pcp)
    {

      for (int i = 0; i < pcp.Layers.Count; ++i)
      {
        var layer = pcp.Layers[i];

        // create a subproject for this layer
        var rq = new RestRequest("/projects");
        rq.AddParameter("text/plain", "Layer " + i, ParameterType.RequestBody);
        var layerProject = pcp.Client.Post<Project>(rq);
        if (layerProject.Data == null)
          throw new Exception("Failed to create subproject.");

        // ensure this layer is a child
        rq = new RestRequest(projectResource + "/" + layerProject.Data.id + "/parentProject");
        rq.AddParameter("application/xml", "<project-ref id=\"" + pcp.Project.id + "\"/>", ParameterType.RequestBody);
        layerProject = pcp.Client.Put<Project>(rq);
        if (layerProject.Data == null)
          throw new Exception("Failed to change layer parent project.");

        // for each of the projects in the layer
        foreach (var pd in layer)
        {
          // create a build configuration
          rq = new RestRequest("/projects/" + layerProject.Data.id + "/buildTypes");
          rq.AddParameter("text/plain", Path.GetFileNameWithoutExtension(pd.Project.FileName), ParameterType.RequestBody);
          var resp= pcp.Client.Post<Configuration>(rq);
          if (resp.Data == null)
            throw new Exception("Failed to create configuration " + layerProject.Data.id);

          // write a configuration
          Configuration conf = resp.Data;
          conf.description = "This configuration builds the project " + Path.GetFileName(pd.Project.FileName) +
                             " which resides in layer " + i + " of the parent project " + pcp.ProjectName;
          rq = new RestRequest("/buildTypes/id:" + conf.id);
          rq.AddBody(conf);
          resp = pcp.Client.Post<Configuration>(rq);
          if (resp.Data == null)
            throw new Exception("Unable to update configuration " + conf.id);
        }
      }
    }

    private static Dictionary<int, ProjectData> GetProjectItemsFromSolution(string solutionPath, out List<VSProject> myProjects)
    {
      var sln = new Solution(solutionPath);
      var projects = sln.GetProjects().ToList();

      var ints = projects.Where(p => p.FileName.Contains("CommonServices.csproj"));

      // convert projects to a ProjectData list
      var projectItems = new Dictionary<int, ProjectData>();
      for (int i = 0; i < projects.Count(); ++i)
      {
        var pd = new ProjectData();
        pd.Id = i;
        pd.Project = projects[i];
        pd.Dependencies = projects[i].ProjectReferences.Select(r =>
          {
            int j = projects.FindIndex(pi => pi.ProjectGuid == r.Id);
            if (j == -1)
            {
              //throw new Exception("The project " + pd.Project.FileName + " references the project " + r.FileName + " which is missing or corrupted.");
            }
            return j;
          }).Where(x => x != -1).ToArray();
        projectItems.Add(pd.Id, pd);
      }
      myProjects = projects;
      return projectItems;
    }

    private static List<int> CreateTopologicalSort(Dictionary<int, ProjectData> projectItems)
    {
      // Build up the dependencies graph
      var dependenciesToFrom = new Dictionary<int, List<int>>();
      var dependenciesFromTo = new Dictionary<int, List<int>>();
      foreach (ProjectData op in projectItems.Values)
      {
        // Note that op.Id depends on each of op.Dependencies
        dependenciesToFrom.Add(op.Id, new List<int>(op.Dependencies));

        // Note that each of op.Dependencies is relied on by op.Id
        foreach (int depId in op.Dependencies)
        {
          List<int> ids;
          if (!dependenciesFromTo.TryGetValue(depId, out ids))
          {
            ids = new List<int>();
            dependenciesFromTo.Add(depId, ids);
          }
          ids.Add(op.Id);
        }
      }

      // Create the sorted list
      var overallPartialOrderingIds = new List<int>(dependenciesToFrom.Count);
      var thisIterationIds = new List<int>(dependenciesToFrom.Count);
      while (dependenciesToFrom.Count > 0)
      {
        thisIterationIds.Clear();
        foreach (var item in dependenciesToFrom)
        {
          // If an item has zero input operations, remove it.
          if (item.Value.Count == 0)
          {
            thisIterationIds.Add(item.Key);

            // Remove all outbound edges
            List<int> depIds;
            if (dependenciesFromTo.TryGetValue(item.Key, out depIds))
            {
              foreach (int depId in depIds)
              {
                dependenciesToFrom[depId].Remove(item.Key);
              }
            }
          }
        }

        // If nothing was found to remove, there's no valid sort.
        if (thisIterationIds.Count == 0) return null;

        // Remove the found items from the dictionary and 
        // add them to the overall ordering
        foreach (int id in thisIterationIds) dependenciesToFrom.Remove(id);
        overallPartialOrderingIds.AddRange(thisIterationIds);
      }

      return overallPartialOrderingIds;
    }


    public class Projects
    {
      public List<Project> project { get; set; }
    }

    [Serializable]
    public class LocalAssemblyInfo
    {
      public string Name { get; set; }
      public string Path { get; set; }
    }

    [Serializable]
    public class ProjectReference
    {
      public Guid Id { get; set; }
      public string FileName { get; set; }
    }


    public class Project
    {
      public string id { get; set; }
      public string name { get; set; }
      public string href { get; set; }
    }

    public class Configuration
    {
      public string id { get; set; }
      public string name { get; set; }
      public string href { get; set; }
      public string description { get; set; }
    }
  }
}