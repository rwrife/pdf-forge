using System.Windows;
using PdfForge.Core;

namespace PdfForge.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StatusText.Text = CoreHealth.GetVersionBanner();
    }
}
