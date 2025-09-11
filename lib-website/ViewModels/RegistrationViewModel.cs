using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Website.ViewModels;

/// <summary>
/// View model for user registration using MVVM Community Toolkit.
/// </summary>
public partial class RegistrationViewModel : ObservableValidator
{
    private readonly ILogger<RegistrationViewModel> _logger;
    
    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]",
        ErrorMessage = "Password must contain uppercase, lowercase, number and special character")]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Please confirm your password")]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match")]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [MaxLength(32, ErrorMessage = "Display name cannot exceed 32 characters")]
    private string? _displayName;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "You must accept the terms of service")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms of service")]
    private bool _acceptTerms;

    [ObservableProperty]
    private bool _acceptMarketing;

    [ObservableProperty]
    private bool _isRegistering;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _registrationSuccessful;

    [ObservableProperty]
    private string? _confirmationEmail;

    public RegistrationViewModel(ILogger<RegistrationViewModel> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Determines if the registration form can be submitted.
    /// </summary>
    public bool CanRegister => !HasErrors && !IsRegistering && AcceptTerms;

    /// <summary>
    /// Submits the registration form.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRegister))]
    public async Task RegisterAsync()
    {
        // Validate all properties
        ValidateAllProperties();
        
        if (HasErrors)
        {
            _logger.LogWarning("Registration validation failed");
            return;
        }

        try
        {
            IsRegistering = true;
            ErrorMessage = null;

            // This will call the Auth service through the website service
            // The actual implementation would be in lib-website-service
            _logger.LogInformation("Submitting registration for {Email}", Email);

            // Simulate registration call
            await Task.Delay(1000); // Replace with actual service call

            RegistrationSuccessful = true;
            ConfirmationEmail = Email;
            
            _logger.LogInformation("Registration successful for {Email}", Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registration failed for {Email}", Email);
            ErrorMessage = "Registration failed. Please try again.";
            RegistrationSuccessful = false;
        }
        finally
        {
            IsRegistering = false;
            RegisterCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Clears the registration form.
    /// </summary>
    [RelayCommand]
    public void ClearForm()
    {
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        DisplayName = null;
        AcceptTerms = false;
        AcceptMarketing = false;
        ErrorMessage = null;
        RegistrationSuccessful = false;
        ConfirmationEmail = null;
        ClearErrors();
    }

    partial void OnEmailChanged(string value)
    {
        ValidateProperty(value, nameof(Email));
        RegisterCommand.NotifyCanExecuteChanged();
    }

    partial void OnPasswordChanged(string value)
    {
        ValidateProperty(value, nameof(Password));
        
        // Revalidate confirm password when password changes
        if (!string.IsNullOrEmpty(ConfirmPassword))
        {
            ValidateProperty(ConfirmPassword, nameof(ConfirmPassword));
        }
        
        RegisterCommand.NotifyCanExecuteChanged();
    }

    partial void OnConfirmPasswordChanged(string value)
    {
        ValidateProperty(value, nameof(ConfirmPassword));
        RegisterCommand.NotifyCanExecuteChanged();
    }

    partial void OnAcceptTermsChanged(bool value)
    {
        ValidateProperty(value, nameof(AcceptTerms));
        RegisterCommand.NotifyCanExecuteChanged();
    }
}