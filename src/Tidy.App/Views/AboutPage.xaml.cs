using System.Reflection;
using Microsoft.UI.Xaml.Controls;

namespace Tidy.App.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)} for Windows";
    }
}
