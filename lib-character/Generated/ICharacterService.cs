using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Service interface for Character API
/// </summary>
[Obsolete]
public partial interface ICharacterService : IDaprService
{
    /// <summary>
    /// CreateCharacter operation
    /// </summary>
    Task<(StatusCodes, CharacterResponse?)> CreateCharacterAsync(CreateCharacterRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetCharacter operation
    /// </summary>
    Task<(StatusCodes, CharacterResponse?)> GetCharacterAsync(GetCharacterRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateCharacter operation
    /// </summary>
    Task<(StatusCodes, CharacterResponse?)> UpdateCharacterAsync(UpdateCharacterRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteCharacter operation
    /// </summary>
    Task<(StatusCodes, object?)> DeleteCharacterAsync(DeleteCharacterRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListCharacters operation
    /// </summary>
    Task<(StatusCodes, CharacterListResponse?)> ListCharactersAsync(ListCharactersRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetCharactersByRealm operation
    /// </summary>
    Task<(StatusCodes, CharacterListResponse?)> GetCharactersByRealmAsync(GetCharactersByRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
