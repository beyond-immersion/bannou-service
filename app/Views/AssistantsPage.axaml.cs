using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace app.Views;

public partial class AssistantsPage : UserControl
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
