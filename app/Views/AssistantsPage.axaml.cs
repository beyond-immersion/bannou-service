using app.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace app.Views;

public partial class AssistantsPage : ReactiveUserControl<AssistantsPageViewModel>
{
    public AssistantsPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
