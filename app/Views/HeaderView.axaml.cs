using app.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace app.Views;

public partial class HeaderView : UserControl
{
    public HeaderView()
    {
        InitializeComponent();
        DataContext = new HeaderViewModel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
