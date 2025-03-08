using ReactiveUI;
using System.Reactive;
using System.Linq;
using app.ViewModels;
using System.Diagnostics;
using System;

namespace app.ViewModels;

public class SidebarViewModel : ViewModelBase
{
    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    public SidebarViewModel()
    {
        NavigateCommand = ReactiveCommand.Create<string>(Navigate);
    }

    private void Navigate(string pageName)
    {
        Debug.WriteLine($"Navigating to page: '{pageName}'");
    }
}
