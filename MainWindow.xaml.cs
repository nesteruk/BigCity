using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BigCity
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
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
      get { return (string) GetValue(ServerUrlProperty); }
      set { SetValue(ServerUrlProperty, value); }
    }

    public string Username
    {
      get { return (string) GetValue(UsernameProperty); }
      set { SetValue(UsernameProperty, value); }
    }

    public string SolutionRelativePath
    {
      get { return (string) GetValue(SolutionRelativePathProperty); }
      set { SetValue(SolutionRelativePathProperty, value); }
    }

    public static readonly DependencyProperty ServerUrlProperty = DependencyProperty.Register("ServerUrl", typeof (string), typeof (MainWindow), new PropertyMetadata(default(string)));
    public static readonly DependencyProperty UsernameProperty = DependencyProperty.Register("Username", typeof (string), typeof (MainWindow), new PropertyMetadata(default(string)));
    public static readonly DependencyProperty SolutionRelativePathProperty = DependencyProperty.Register("SolutionRelativePath", typeof (string), typeof (MainWindow), new PropertyMetadata(default(string)));

    public MainWindow()
    {
      InitializeComponent();

#if DEBUG
      ServerUrl = "http://localhost:81/";
      Username = "dnesteruk";
      TbPassword.Password = "trustno1";
#endif
    }

    private void BtnBrowserForSolution_OnClick(object sender, RoutedEventArgs e)
    {
      var ofd = new OpenFileDialog { Filter = "Visual Studio Solution Files (*.sln)|*.sln" };
      if (ofd.ShowDialog() ?? false)
      {
        SolutionPath = ofd.FileName;
        ProjectName = Path.GetFileNameWithoutExtension(ofd.FileName);
      }
    }

    private void BtnCreateProject_Click(object sender, RoutedEventArgs e)
    {
#if RELEASE
      try
      {
#endif
        Available = false;
        var pcp = new ProjectCreationParams()
        {
          ServerUrl = ServerUrl,
          Username = Username,
          Password = TbPassword.Password,
          ProjectName = ProjectName,
          SolutionPath = SolutionPath,
          SolutionRelativePath = SolutionRelativePath
        };
        ProjectCreator.Start(pcp);
#if RELEASE
      }
      catch (Exception ex)
      {
        MessageBox.Show(this,
          ex.Message,
          "Failed to Create Project",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
      finally
      {
#endif
        Available = true;
        Application.Current.Shutdown();
#if RELEASE
      }
#endif
    }
  }
}
