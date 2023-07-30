using Newtonsoft.Json;

namespace BeyondImmersion.BannouService.Models;

/// <summary>
/// The interface for an account definition data model.
/// See: <see cref="AccountModel"/> and <see cref="AccountService"/>
/// </summary>
public interface IAccountModel
{
    string ID { get; }
    string Email { get; }
    string HashedSecret { get; }
    string DisplayName { get; }
}
