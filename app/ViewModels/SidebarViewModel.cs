using ReactiveUI;
using System.Reactive;
using System.Linq;
using app.ViewModels;

namespace app.ViewModels;

public class SidebarViewModel : ViewModelBase
{
    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    public SidebarViewModel()
    {
        NavigateCommand = ReactiveCommand.Create<string>(param => { /* Change page logic here */ });
    }
}
