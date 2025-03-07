using ReactiveUI;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;

namespace app.ViewModels;

public class HeaderViewModel : ViewModelBase
{
    public ObservableCollection<string> Organizations { get; } = ["Org1", "Org2"];
    public ObservableCollection<string> Projects { get; } = ["Project1", "Project2"];
    public ObservableCollection<string> UserAccounts { get; } = ["User1", "User2"];

    private string? _selectedOrganization;
    public string? SelectedOrganization
    {
        get => _selectedOrganization;
        set => this.RaiseAndSetIfChanged(ref _selectedOrganization, value);
    }

    private string? _selectedProject;
    public string? SelectedProject
    {
        get => _selectedProject;
        set => this.RaiseAndSetIfChanged(ref _selectedProject, value);
    }

    private string? _selectedUserAccount;
    public string? SelectedUserAccount
    {
        get => _selectedUserAccount;
        set => this.RaiseAndSetIfChanged(ref _selectedUserAccount, value);
    }

    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    public HeaderViewModel()
    {
        NavigateCommand = ReactiveCommand.Create<string>(param => { /* Navigation logic here */ });
    }
}
