using ReactiveUI;
using System.Reactive;
using System.Linq;
using app.ViewModels;
using System.Collections.ObjectModel;

namespace app.ViewModels;

public class AssistantsPageViewModel : ViewModelBase
{
    public ObservableCollection<string> Messages { get; } = [];
    public ObservableCollection<string> ApiLogs { get; } = [];

    public AssistantsPageViewModel()
    {
        // Bind data or handle logic for assistants page here
    }
}
