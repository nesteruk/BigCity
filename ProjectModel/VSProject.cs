using System;
using System.Collections.Generic;

namespace BigCity.ProjectModel
{
  [Serializable]
  public class VSProject : IEquatable<VSProject>
  {
    public string FileName { get; set; }
    public Guid ProjectGuid { get; set; }
    public string OutputFolder { get; set; }
    public Dictionary<string, string> BuildParameters { get; set; }
    public bool BuildReferences { get; set; }
    public List<string> GACReferences { get; set; }
    public List<ProjectCreator.LocalAssemblyInfo> LocalReferences { get; set; }
    public List<ProjectCreator.ProjectReference> ProjectReferences { get; set; }

    public bool Equals(VSProject other)
    {
      if (ReferenceEquals(null, other)) return false;
      if (ReferenceEquals(this, other)) return true;
      return other.ProjectGuid.Equals(ProjectGuid);
    }

    public override bool Equals(object obj)
    {
      if (ReferenceEquals(null, obj)) return false;
      if (ReferenceEquals(this, obj)) return true;
      if (obj.GetType() != typeof(ProjectCreator.Project)) return false;
      return Equals((ProjectCreator.Project)obj);
    }

    public override int GetHashCode()
    {
      return ProjectGuid.GetHashCode();
    }

    public static bool operator ==(VSProject left, VSProject right)
    {
      return Equals(left, right);
    }

    public static bool operator !=(VSProject left, VSProject right)
    {
      return !Equals(left, right);
    }
  }
}