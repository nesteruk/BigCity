using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
      pcp.Log.Status = "Checking for projects with same name";
      pcp.Client = new RestClient(restRoot);
      pcp.Client.Authenticator = new HttpBasicAuthenticator(pcp.Username, pcp.Password);
      var rq = new RestRequest(projectResource, Method.GET);
      var resp = pcp.Client.Get<Projects>(rq);
      if (resp.Data == null)
      {
        pcp.Log.ErrorMessage = "Failed to connect. Check server URL, username and password.";
        return;
      }

      var ps = resp.Data.project;
      foreach (var serverProject in ps)
      {
        if (serverProject.name.Equals(pcp.ProjectName))
        {
          #if DEBUG
          pcp.Log.Status = "Deleting project " + serverProject.name;
          var drq = new RestRequest(projectResource + "/" + pcp.ProjectName, Method.DELETE);
          pcp.Client.Execute(drq);
          #else
          pcp.Log.ErrorMessage = "A project with this name already exists on the server.";
          return;
          #endif
        }
      }

      CreateProject(pcp);
    }

    private static void CreateProject(ProjectCreationParams pcp)
    {
      pcp.Log.Status = "Creating new project";
      var rq = new RestRequest(projectResource);
      rq.AddParameter("text/plain", pcp.ProjectName, ParameterType.RequestBody);
      var resp = pcp.Client.Post<Project>(rq);
      pcp.Project = resp.Data;
      if (pcp.Project == null)
      {
        pcp.Log.ErrorMessage = "Failed to create project. Check the user has appropriate rights.";
        return;
      }

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
      pcp.Log.Status = "Building solution graph";
      List<VSProject> projects;
      var items = GetProjectItemsFromSolution(pcp.SolutionPath, out projects);
      var projectData = items.Values.ToList();
      
      // let's build execution layers here and now
      var layers = new List<HashSet<ProjectData>>();
      int currentLayer = 0;
      do
      {
        pcp.Log.Status = "Analyzing layer " + currentLayer;
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
            pcp.Log.ErrorMessage = "Failed to find elements for layer " + currentLayer;
            return;
          }
        }
        layers.Add(thisLayer);
        currentLayer++;
      } while (projectData.Count > 0);

    build:
      pcp.Layers = layers;
      CreateBuildConfigurations(pcp);
    }

    public static String MakeRelativePath(String fromPath, String toPath)
    {
      if (String.IsNullOrEmpty(fromPath)) throw new ArgumentNullException("fromPath");
      if (String.IsNullOrEmpty(toPath)) throw new ArgumentNullException("toPath");

      var fromUri = new Uri(fromPath);
      Uri toUri = new Uri(toPath);

      Uri relativeUri = fromUri.MakeRelativeUri(toUri);
      String relativePath = Uri.UnescapeDataString(relativeUri.ToString());

      //return relativePath.Replace('/', Path.DirectorySeparatorChar);
      return relativePath;
    }

    private static void CreateBuildConfigurations(ProjectCreationParams pcp)
    {
      var configurationNames = new Dictionary<int,Configuration>();

      //Parallel.For(0, pcp.Layers.Count, i =>
      for (int i = 0; i < pcp.Layers.Count; ++i)
      {
        var layer = pcp.Layers[i];

        // create a subproject for this layer
        pcp.Log.Status = "Creating subproject for layer " + i;
        var rq = new RestRequest("/projects");
        rq.AddParameter("text/plain", "Layer " + i, ParameterType.RequestBody);
        var layerProject = pcp.Client.Post<Project>(rq);
        if (layerProject.Data == null)
        {
          pcp.Log.ErrorMessage = "Failed to create subproject.";
          return;
        }

        // ensure this layer is a child
        pcp.Log.Status = "Setting parent of layer " + i;
        rq = new RestRequest(projectResource + "/" + layerProject.Data.id + "/parentProject");
        rq.AddParameter("application/xml", "<project-ref id=\"" + pcp.Project.id + "\"/>", ParameterType.RequestBody);
        layerProject = pcp.Client.Put<Project>(rq);
        if (layerProject.Data == null)
        {
          pcp.Log.ErrorMessage = "Failed to change layer parent project.";
          return;
        }

        // for each of the projects in the layer
        // this is *not* parallelizable
        foreach (var pd in layer)
        {
          // create a build configuration
          pcp.Log.Status = "Creating build config for " + layerProject.Data.name;
          rq = new RestRequest("/projects/" + layerProject.Data.id + "/buildTypes");
          rq.AddParameter("text/plain", Path.GetFileNameWithoutExtension(pd.Project.FileName), ParameterType.RequestBody);
          var resp = pcp.Client.Post<Configuration>(rq);
          if (resp.Data == null)
          {
            pcp.Log.ErrorMessage = "Failed to create configuration " + layerProject.Data.id;
            return;
          }

          // write a configuration
          Configuration conf = resp.Data;
          configurationNames.Add(pd.Id, conf);
          conf.description = "This configuration builds the project " + Path.GetFileName(pd.Project.FileName) +
                             " which resides in layer " + i + " of the parent project " + pcp.ProjectName;
          rq = new RestRequest("/buildTypes/id:" + conf.id);
          rq.AddBody(conf);
          resp = pcp.Client.Post<Configuration>(rq);

          // add artifact rules
          rq = new RestRequest("/buildTypes/id:" + conf.id + "/settings");
          var relativeOutputFolder = "\\" + 
            MakeRelativePath(pcp.SolutionPath, Path.GetDirectoryName(pd.Project.FileName))
            + "\\" +pd.Project.OutputFolder;
          var e = "<properties><property name=\"artifactRules\" value=\"" +
            relativeOutputFolder.Replace('\\','/') +
            "*.*\"/></properties>";
          rq.AddParameter("application/xml", e, ParameterType.RequestBody);
          var _ = pcp.Client.Put<object>(rq);


          // add build root entry
          string entry = @"
      <vcs-root-entry id=""Experimental_FooAndBar_LocalHg"">
         <vcs-root id=""Experimental_FooAndBar_LocalHg"" name=""Local HG"" href=""/httpAuth/app/rest/vcs-roots/id:Experimental_FooAndBar_LocalHg""/>
         <checkout-rules/>
      </vcs-root-entry>";
          rq = new RestRequest("/buildTypes/id:" + conf.id + "/vcs-root-entries");
          rq.AddParameter("application/xml", entry, ParameterType.RequestBody);
          resp = pcp.Client.Post<Configuration>(rq);

          // add a build step
          var template = @"<step name="""" type=""MSBuild"">
      <properties>
         <property name=""build-file-path"" value=""{0}""/>
         <property name=""msbuild_version"" value=""4.5""/>
         <property name=""run-platform"" value=""x64""/>
         <property name=""runnerArgs"" value=""/p:BuildProjectReferences=false""/>
         <property name=""targets"" value=""{1}""/>
         <property name=""teamcity.step.mode"" value=""default""/>
         <property name=""toolsVersion"" value=""4.0""/>
      </properties>
   </step>";
          string step = string.Format(template, Path.GetFileName(pcp.SolutionPath),
            Path.GetFileNameWithoutExtension(pd.Project.FileName).Replace('.','_'));
          rq = new RestRequest("/buildTypes/id:" + conf.id + "/steps");
          rq.AddParameter("application/xml", step, ParameterType.RequestBody);
          var __ = pcp.Client.Post<object>(rq);
          
          // create dependencies
          const string sd = @"<snapshot-dependency type=""snapshot_dependency"">
         <properties>
            <property name=""run-build-if-dependency-failed"" value=""false""/>
            <property name=""run-build-on-the-same-agent"" value=""false""/>
            <property name=""take-started-build-with-same-revisions"" value=""true""/>
            <property name=""take-successful-builds-only"" value=""true""/>
         </properties>
         {0}
      </snapshot-dependency>";
          const string ad = @"<artifact-dependency type=""artifact_dependency"">
         <properties>
            <property name=""cleanDestinationDirectory"" value=""false""/>
            <property name=""pathRules"" value=""*.*=>{1}""/>
            <property name=""revisionName"" value=""sameChainOrLastFinished""/>
            <property name=""revisionValue"" value=""latest.sameChainOrLastFinished""/>
         </properties>
         {0}
      </artifact-dependency>";
          foreach (var dependencyId in pd.Dependencies)
          {
            // snapshot dependency
            string sdep = string.Format(sd, configurationNames[dependencyId].SourceBuildTypeXML);
            rq = new RestRequest("/buildTypes/id:" + conf.id + "/snapshot-dependencies");
            rq.AddParameter("application/xml", sdep, ParameterType.RequestBody);
            pcp.Client.Post<SnapshotDependency>(rq);
            
            // artifact dependency
            // we need the relative output folder of _dependency_, not this item
            
            // find project in previous layers
            var p = pcp.Layers.SelectMany(x => x).First(z => z.Id == dependencyId);

            var relativeOutputFolderOfDependency = "\\" +
            MakeRelativePath(pcp.SolutionPath, Path.GetDirectoryName(
              p.Project.FileName
            ))
            + "\\" + pd.Project.OutputFolder;

            string adep = string.Format(ad, configurationNames[dependencyId].SourceBuildTypeXML,
              relativeOutputFolderOfDependency);
            rq = new RestRequest("/buildTypes/id:" + conf.id + "/artifact-dependencies");
            rq.AddParameter("application/xml", adep, ParameterType.RequestBody);
            pcp.Client.Post<ArtifactDependency>(rq);
          }
        }
      };
    }

    private static Dictionary<int, ProjectData> GetProjectItemsFromSolution(string solutionPath, out List<VSProject> myProjects)
    {
      var sln = new Solution(solutionPath);
      var projects = sln.GetProjects().ToList();

      // convert projects to a ProjectData list
      var projectItems = new Dictionary<int, ProjectData>();
      for (int i = 0; i < projects.Count(); ++i)
      {
        var pd = new ProjectData
        {
          Id = i,
          Project = projects[i],
          Dependencies = projects[i].ProjectReferences
            .Select(r => projects.FindIndex(pi => pi.ProjectGuid == r.Id))
            .Where(x => x != -1).ToArray()
        };
        projectItems.Add(pd.Id, pd);
      }
      myProjects = projects;
      return projectItems;
    }

    /// <summary>
    /// A topological sort is only useful if we want to schedule compilation in the 
    /// most efficient manner. And this is something that we have little control over.
    /// </summary>
    /// <param name="projectItems"></param>
    /// <returns></returns>
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

      public string SourceBuildTypeXML
      {
        get
        {
          return string.Format("<source-buildType id=\"{0}\" name=\"{1}\" href=\"{2}\"/>",
            id, name, href);
        }
      }
    }
  }

  internal class Property
  {
    public string name { get; set; }
    public string value { get; set; }
  }

  internal class ArtifactDependency
  {
  }

  internal class SnapshotDependency
  {
    public string id { get; set; }
    public string type { get; set; }
    
  }
}