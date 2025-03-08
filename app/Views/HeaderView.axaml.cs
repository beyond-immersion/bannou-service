using app.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace app.Views;

public partial class HeaderView : ReactiveUserControl<HeaderViewModel>
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
