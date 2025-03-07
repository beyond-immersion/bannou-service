using app.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace app.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
        DataContext = new SidebarViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
