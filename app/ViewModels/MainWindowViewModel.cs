using app.ViewModels;
using app.Views;
using Avalonia.Controls;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive;
using System.Reflection;

namespace app.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private bool _isChatSelected;
    public bool IsChatSelected
    {
        get => _isChatSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _isChatSelected, value);
        }
    }

    private bool _isRealtimeSelected;
    public bool IsRealtimeSelected
    {
        get => _isRealtimeSelected;
        set => this.RaiseAndSetIfChanged(ref _isRealtimeSelected, value);
    }

    private bool _isAssistantsSelected;
    public bool IsAssistantsSelected
    {
        get => _isAssistantsSelected;
        set => this.RaiseAndSetIfChanged(ref _isAssistantsSelected, value);
    }

    private bool _isTTSSelected;
    public bool IsTTSSelected
    {
        get => _isTTSSelected;
        set => this.RaiseAndSetIfChanged(ref _isTTSSelected, value);
    }

    private bool _isHelpSelected;
    public bool IsHelpSelected
    {
        get => _isHelpSelected;
        set => this.RaiseAndSetIfChanged(ref _isHelpSelected, value);
    }

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
        NavigateCommand = ReactiveCommand.Create<string>(Navigate);
        Navigate("Chat");
    }

    private void ResetSelection()
    {
        IsChatSelected = false;
        IsRealtimeSelected = false;
        IsAssistantsSelected = false;
        IsTTSSelected = false;
        IsHelpSelected = false;
    }

    private void Navigate(string? pageName)
    {
        if (string.IsNullOrWhiteSpace(pageName))
            return;

        var propInfo = GetType().GetProperty($"Is{pageName}Selected", BindingFlags.Public | BindingFlags.Instance);
        if (propInfo != null)
        {
            ResetSelection();
            propInfo.SetValue(this, true);
            ChangePage(pageName);

            Debug.WriteLine($"Navigating to page: '{pageName}'");
        }
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
        }
    }
}
