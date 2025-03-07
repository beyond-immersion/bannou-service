using app.ViewModels;
using app.Views;
using Avalonia.Controls;
using ReactiveUI;
using System.Reactive;

namespace app.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private UserControl? _currentPageView;
    public UserControl? CurrentPageView
    {
        get => _currentPageView;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentPageView, value);
        }
    }

    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    public MainWindowViewModel()
    {
        NavigateCommand = ReactiveCommand.Create<string>(ChangePage);
        ChangePage("Chat"); // Default page
    }

    private void ChangePage(string pageName)
    {
        switch (pageName)
        {
            case "Chat":
                CurrentPageView = new AssistantsPage();
                break;
            case "Realtime":
                CurrentPageView = new AssistantsPage();
                break;
            case "Assistants":
                CurrentPageView = new AssistantsPage();
                break;
            case "TTS":
                CurrentPageView = new AssistantsPage();
                break;
            case "Help":
                CurrentPageView = new AssistantsPage();
                break;
                // Add additional cases as necessary.
        }
    }
}
