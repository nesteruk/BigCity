using System;
using System.Diagnostics;

namespace BigCity.ProjectModel
{
  [DebuggerDisplay("{Id} {Project.FileName}")]
  public class ProjectData
  {
    internal int[] Dependencies;

    internal int Id { get; set; }

    internal VSProject Project;
  }
}