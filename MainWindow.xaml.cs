using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Win32;
using RestSharp;
using RestSharp.Deserializers;

namespace BigCity
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window, ILog
  {
    public bool Available
    {
      get { return (bool)GetValue(AvailableProperty); }
      set { SetValue(AvailableProperty, value); }
    }

    public static readonly DependencyProperty AvailableProperty =
        DependencyProperty.Register("Available", typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

    public string SolutionPath
    {
      get { return (string)GetValue(SolutionPathProperty); }
      set { SetValue(SolutionPathProperty, value); }
    }

    public string ProjectName
    {
      get { return (string)GetValue(ProjectNameProperty); }
      set { SetValue(ProjectNameProperty, value); }
    }

    public static readonly DependencyProperty SolutionPathProperty =
      DependencyProperty.Register("SolutionPath", typeof(string), typeof(MainWindow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ProjectNameProperty =
      DependencyProperty.Register("ProjectName", typeof(string), typeof(MainWindow), new PropertyMetadata(string.Empty));

    public string ServerUrl
    {
      get { return (string)GetValue(ServerUrlProperty); }
      set { SetValue(ServerUrlProperty, value); }
    }

    public string Username
    {
      get { return (string)GetValue(UsernameProperty); }
      set { SetValue(UsernameProperty, value); }
    }

    public string SolutionRelativePath
    {
      get { return (string)GetValue(SolutionRelativePathProperty); }
      set { SetValue(SolutionRelativePathProperty, value); }
    }

    public static readonly DependencyProperty ServerUrlProperty = 
      DependencyProperty.Register("ServerUrl", typeof(string), typeof(MainWindow), new PropertyMetadata(default(string)));
    public static readonly DependencyProperty UsernameProperty = 
      DependencyProperty.Register("Username", typeof(string), typeof(MainWindow), new PropertyMetadata(default(string)));
    public static readonly DependencyProperty SolutionRelativePathProperty = 
      DependencyProperty.Register("SolutionRelativePath", typeof(string), typeof(MainWindow), new PropertyMetadata(default(string)));

    public static readonly DependencyProperty VcsRootsProperty = 
      DependencyProperty.Register("VcsRoots", typeof (ObservableCollection<VcsRoot>), typeof (MainWindow), 
      new PropertyMetadata(new ObservableCollection<VcsRoot>()));

    public MainWindow()
    {
      InitializeComponent();

#if DEBUG
      ServerUrl = "http://localhost:81/";
      Username = "admin";
      TbPassword.Password = "admin";
#endif
    }

    private void BtnBrowserForSolution_OnClick(object sender, RoutedEventArgs e)
    {
      var ofd = new OpenFileDialog { Filter = "Visual Studio Solution Files (*.sln)|*.sln" };
      if (ofd.ShowDialog() ?? false)
      {
        SolutionPath = ofd.FileName;
        ProjectName = Path.GetFileNameWithoutExtension(ofd.FileName);
        SolutionRelativePath = GetSolutionRelativePath(ofd.FileName);
      }
    }

    private string GetSolutionRelativePath(string fileName)
    {
      // umm...
      var thisDir = new DirectoryInfo(Path.GetDirectoryName(fileName));
      for (var dir = thisDir; dir != null; dir = Directory.GetParent(dir.FullName))
      {
        if (Directory.Exists(dir + "\\.hg") || Directory.Exists(dir + "\\.git"))
        {
          return ProjectCreator.MakeRelativePath(dir.FullName, thisDir.FullName);
        }
      }
      return null;
    }

    private void BtnCreateProject_Click(object sender, RoutedEventArgs e)
    {
      Available = false;
      var pcp = new ProjectCreationParams
      {
        ServerUrl = ServerUrl,
        Username = Username,
        Password = TbPassword.Password,
        ProjectName = ProjectName,
        SolutionPath = SolutionPath,
        SolutionRelativePath = SolutionRelativePath,
        Log = this
      };
      Action a = () => ProjectCreator.Start(pcp);
      a.BeginInvoke(ar => Dispatcher.Invoke(() => {
        Available = true;
        TbSubmit.Text = "Done creating project '" + ProjectName + "'. Enjoy!";
      }), null);

    }

    public string Status
    {
      set { Dispatcher.Invoke(() => TbSubmit.Text = value); }
    }

    public string ErrorMessage
    {
      set
      {
        Dispatcher.Invoke(() =>
          MessageBox.Show(this, value, "Project creation failed",
            MessageBoxButton.OK, MessageBoxImage.Error));
      }
    }

    public ObservableCollection<VcsRoot> VcsRoots
    {
      get { return (ObservableCollection<VcsRoot>) GetValue(VcsRootsProperty); }
      set { SetValue(VcsRootsProperty, value); }
    }

    private void GetVcsRoots(object sender, RoutedEventArgs e)
    {
      // todo: validate url/username/password
      string restRoot = ServerUrl + (ServerUrl.EndsWith("/") ? String.Empty : "/") + "httpAuth/app/rest/";
      string path = restRoot + "vcs-roots";
      var client = new RestClient(path);
      client.Authenticator = new HttpBasicAuthenticator(Username, TbPassword.Password);
      var rq = new RestRequest();
      var resp = client.Get<VcsRoots>(rq);
      if (resp.Data != null)
      {
        VcsRoots.Clear();
        foreach (var root in resp.Data.vcsroot)
          VcsRoots.Add(root);
      }
    }
  }

  internal class VcsRoots
  {
    [DeserializeAs(Name="vcs-root", Attribute = true)]
    public List<VcsRoot> vcsroot { get; set; }
  }

  [DeserializeAs(Name="vcs-root")]
  public class VcsRoot
  {
    public string id { get; set; }
    public string name { get; set; }
    public string href { get; set; }
  }
}
