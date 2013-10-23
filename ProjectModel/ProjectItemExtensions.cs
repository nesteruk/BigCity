using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Execution;

namespace BigCity.ProjectModel
{
  public static class ProjectItemExtensions
  {
    public static Guid GetProjectGuid(this ProjectInstance p)
    {
      return Guid.Parse(p.GetPropertyValue("ProjectGuid"));
    }

    /// <summary>
    /// Gets the names of all the GAC assemblies that are referenced by the specified project instance.
    /// </summary>
    public static List<string> GetGACProjectReferences(this ProjectInstance p)
    {
      return p.Items
        .Where(x => x.ItemType == "Reference" && x.Metadata.All(y => y.Name != "HintPath"))
        .Select(x => x.EvaluatedInclude).ToList();
    }

    public static List<ProjectCreator.LocalAssemblyInfo> GetLocalLibraryReferences(this ProjectInstance p)
    {
      IEnumerable<ProjectItemInstance> references = p.Items.Where(x => x.ItemType == "Reference" &&
                                                                       x.Metadata.Any(y => y.Name == "HintPath"));
      return references.Select(x =>
        new ProjectCreator.LocalAssemblyInfo
        {
          Name = x.EvaluatedInclude,
          Path = x.Metadata.Single(y => y.Name == "HintPath").EvaluatedValue
        }).ToList();
    }

    public static List<ProjectCreator.ProjectReference> GetProjectReferences(this ProjectInstance p)
    {
      var references = new List<ProjectCreator.ProjectReference>();
      foreach (var i in p.Items)
      {
        if (i.ItemType.Equals("ProjectReference"))
        {
          var proj = i.Metadata.SingleOrDefault(z => z.Name.Equals("Project"));
          if (proj != null)
          {
            references.Add(new ProjectCreator.ProjectReference{
              Id=Guid.Parse(proj.EvaluatedValue),
              FileName = i.EvaluatedInclude
            });
          }
          else
          {
            var include = i.EvaluatedInclude;
            // try to get physical file and projectguid from that
            string dir = Path.GetDirectoryName(p.FullPath);
            string projPath = Path.GetFullPath(Path.Combine(dir, include));
            if (File.Exists(projPath))
            {
              var pi = new ProjectInstance(projPath);
              var item = pi.ToProjectItem();
              references.Add(new ProjectCreator.ProjectReference {Id = item.ProjectGuid, FileName = projPath});
            }
            else
            {
              // hack: just don't reference this
              //throw new Exception("Project " + p.FullPath + " references non-existent project " + projPath);
            }
          }
        }
      }

      return references;
    }

    public static VSProject ToProjectItem(this ProjectInstance pi)
    {
      var res = new VSProject
      {
        FileName = pi.FullPath,
        ProjectGuid = pi.GetProjectGuid(),
        GACReferences = pi.GetGACProjectReferences(),
        LocalReferences = pi.GetLocalLibraryReferences(),
        ProjectReferences = pi.GetProjectReferences()
      };
      return res;
    }
  }
}