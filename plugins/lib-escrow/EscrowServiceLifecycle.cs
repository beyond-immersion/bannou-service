using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Lifecycle operations for escrow management.
/// Handles creation, retrieval, and listing of escrow agreements.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Creates a new escrow agreement.
    /// </summary>
    public async Task<(StatusCodes, CreateEscrowResponse?)> CreateEscrowAsync(
        CreateEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body.Parties == null || body.Parties.Count < 2 || body.Parties.Count > _configuration.MaxParties)
        {
            return (StatusCodes.BadRequest, null);
        }

        if (body.ExpectedDeposits == null || body.ExpectedDeposits.Count == 0)
        {
            return (StatusCodes.BadRequest, null);
        }

        var escrowId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        if (body.TrustMode == EscrowTrustMode.SinglePartyTrusted && body.TrustedPartyId == null)
        {
            return (StatusCodes.BadRequest, null);
        }

        // Parse ISO 8601 duration from configuration for default and max timeout
        var defaultTimeout = XmlConvert.ToTimeSpan(_configuration.DefaultTimeout);
        var maxTimeout = XmlConvert.ToTimeSpan(_configuration.MaxTimeout);
        var expiresAt = body.ExpiresAt ?? now.Add(defaultTimeout);

        // Validate that the requested timeout doesn't exceed the maximum allowed duration
        var requestedDuration = expiresAt - now;
        if (requestedDuration > maxTimeout)
        {
            _logger.LogWarning("Escrow creation rejected: requested duration {Duration} exceeds max {MaxTimeout}",
                requestedDuration, maxTimeout);
            return (StatusCodes.BadRequest, null);
        }

        // Validate MaxPendingPerParty limits for all parties
        foreach (var partyInput in body.Parties)
        {
            var partyKey = GetPartyPendingKey(partyInput.PartyId, partyInput.PartyType);
            var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
            if (existingCount != null && existingCount.PendingCount >= _configuration.MaxPendingPerParty)
            {
                _logger.LogWarning("Escrow creation rejected: party {PartyType}:{PartyId} has {Count} pending escrows, max is {Max}",
                    partyInput.PartyType, partyInput.PartyId, existingCount.PendingCount, _configuration.MaxPendingPerParty);
                return (StatusCodes.BadRequest, null);
            }
        }

        var partyModels = new List<EscrowPartyModel>();
        var tokenRecordsToSave = new List<TokenHashModel>();
        var depositTokens = new List<PartyToken>();

        foreach (var partyInput in body.Parties)
        {
            var partyModel = new EscrowPartyModel
            {
                PartyId = partyInput.PartyId,
                PartyType = partyInput.PartyType,
                DisplayName = partyInput.DisplayName,
                Role = partyInput.Role,
                ConsentRequired = partyInput.ConsentRequired ?? DetermineConsentRequired(partyInput.Role),
                WalletId = partyInput.WalletId,
                ContainerId = partyInput.ContainerId,
                DepositTokenUsed = false,
                ReleaseTokenUsed = false
            };

            if (body.TrustMode == EscrowTrustMode.FullConsent)
            {
                var hasExpectedDeposit = body.ExpectedDeposits.Any(ed =>
                    ed.PartyId == partyInput.PartyId && ed.PartyType == partyInput.PartyType);

                if (hasExpectedDeposit)
                {
                    var depositToken = GenerateToken(escrowId, partyInput.PartyId, TokenType.Deposit);
                    partyModel.DepositToken = depositToken;

                    var tokenHash = HashToken(depositToken);
                    tokenRecordsToSave.Add(new TokenHashModel
                    {
                        TokenHash = tokenHash,
                        EscrowId = escrowId,
                        PartyId = partyInput.PartyId,
                        TokenType = TokenType.Deposit,
                        CreatedAt = now,
                        ExpiresAt = expiresAt,
                        Used = false
                    });

                    depositTokens.Add(new PartyToken
                    {
                        PartyId = partyInput.PartyId,
                        PartyType = partyInput.PartyType,
                        Token = depositToken
                    });
                }

                if (partyModel.ConsentRequired)
                {
                    var releaseToken = GenerateToken(escrowId, partyInput.PartyId, TokenType.Release);
                    partyModel.ReleaseToken = releaseToken;

                    var tokenHash = HashToken(releaseToken);
                    tokenRecordsToSave.Add(new TokenHashModel
                    {
                        TokenHash = tokenHash,
                        EscrowId = escrowId,
                        PartyId = partyInput.PartyId,
                        TokenType = TokenType.Release,
                        CreatedAt = now,
                        ExpiresAt = expiresAt,
                        Used = false
                    });
                }
            }

            partyModels.Add(partyModel);
        }

        var expectedDepositModels = body.ExpectedDeposits.Select(ed => new ExpectedDepositModel
        {
            PartyId = ed.PartyId,
            PartyType = ed.PartyType,
            ExpectedAssets = ed.ExpectedAssets?.Select(MapAssetInputToModel).ToList(),
            Optional = ed.Optional ?? false,
            DepositDeadline = ed.DepositDeadline,
            Fulfilled = false
        }).ToList();

        List<ReleaseAllocationModel>? releaseAllocationModels = null;
        if (body.ReleaseAllocations != null && body.ReleaseAllocations.Count > 0)
        {
            releaseAllocationModels = body.ReleaseAllocations.Select(ra => new ReleaseAllocationModel
            {
                RecipientPartyId = ra.RecipientPartyId,
                RecipientPartyType = ra.RecipientPartyType,
                Assets = ra.Assets?.Select(MapAssetInputToModel).ToList(),
                DestinationWalletId = ra.DestinationWalletId,
                DestinationContainerId = ra.DestinationContainerId
            }).ToList();
        }

        var requiredConsents = body.RequiredConsentsForRelease ?? -1;
        if (requiredConsents == -1)
        {
            requiredConsents = partyModels.Count(p => p.ConsentRequired);
        }

        var agreementModel = new EscrowAgreementModel
        {
            EscrowId = escrowId,
            EscrowType = body.EscrowType,
            TrustMode = body.TrustMode,
            TrustedPartyId = body.TrustedPartyId,
            TrustedPartyType = body.TrustedPartyType,
            Parties = partyModels,
            ExpectedDeposits = expectedDepositModels,
            Deposits = new List<EscrowDepositModel>(),
            ReleaseAllocations = releaseAllocationModels,
            BoundContractId = body.BoundContractId,
            Consents = new List<EscrowConsentModel>(),
            Status = EscrowStatus.PendingDeposits,
            RequiredConsentsForRelease = requiredConsents,
            CreatedAt = now,
            ExpiresAt = expiresAt,
            ReferenceType = body.ReferenceType,
            ReferenceId = body.ReferenceId,
            Description = body.Description,
            Metadata = body.Metadata,
            ReleaseMode = body.ReleaseMode ?? _configuration.DefaultReleaseMode,
            RefundMode = body.RefundMode ?? _configuration.DefaultRefundMode
        };

        var agreementKey = GetAgreementKey(escrowId);
        await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken: cancellationToken);

        foreach (var tokenRecord in tokenRecordsToSave)
        {
            var tokenKey = GetTokenKey(tokenRecord.TokenHash);
            await TokenStore.SaveAsync(tokenKey, tokenRecord, cancellationToken: cancellationToken);
        }

        var statusIndexKey = $"{GetStatusIndexKey(EscrowStatus.PendingDeposits)}:{escrowId}";
        var statusEntry = new StatusIndexEntry
        {
            EscrowId = escrowId,
            Status = EscrowStatus.PendingDeposits,
            ExpiresAt = expiresAt,
            AddedAt = now
        };
        await StatusIndexStore.SaveAsync(statusIndexKey, statusEntry, cancellationToken: cancellationToken);

        // Track incremented parties for rollback on failure
        var incrementedParties = new List<(Guid PartyId, EntityType PartyType)>();
        try
        {
            foreach (var party in partyModels)
            {
                await IncrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
                incrementedParties.Add((party.PartyId, party.PartyType));
            }

            var createdEvent = new EscrowCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = escrowId,
                EscrowType = agreementModel.EscrowType,
                TrustMode = agreementModel.TrustMode,
                Parties = partyModels.Select(p => new EscrowPartyInfo
                {
                    PartyId = p.PartyId,
                    PartyType = p.PartyType,
                    Role = p.Role
                }).ToList(),
                ExpectedDepositCount = expectedDepositModels.Count,
                ExpiresAt = expiresAt,
                BoundContractId = body.BoundContractId,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId,
                CreatedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowCreated, createdEvent, cancellationToken);

            _logger.LogInformation(
                "Created escrow {EscrowId} with {PartyCount} parties, type {EscrowType}, trust mode {TrustMode}",
                escrowId, partyModels.Count, body.EscrowType, body.TrustMode);

            return (StatusCodes.OK, new CreateEscrowResponse
            {
                Escrow = MapToApiModel(agreementModel),
                DepositTokens = depositTokens
            });
        }
        catch (Exception postSaveEx)
        {
            // Rollback party pending counts on failure after increment
            _logger.LogWarning(postSaveEx, "Failed after incrementing pending counts for escrow {EscrowId}, rolling back {Count} parties",
                escrowId, incrementedParties.Count);
            foreach (var (partyId, partyType) in incrementedParties)
            {
                await DecrementPartyPendingCountAsync(partyId, partyType, cancellationToken);
            }
            throw; // Re-throw to be caught by outer catch
        }
    }

    /// <summary>
    /// Gets an escrow agreement by ID.
    /// </summary>
    public async Task<(StatusCodes, GetEscrowResponse?)> GetEscrowAsync(
        GetEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = GetAgreementKey(body.EscrowId);
        var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

        if (agreementModel == null)
        {
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, new GetEscrowResponse
        {
            Escrow = MapToApiModel(agreementModel)
        });
    }

    /// <summary>
    /// Lists escrow agreements with optional filtering.
    /// </summary>
    public async Task<(StatusCodes, ListEscrowsResponse?)> ListEscrowsAsync(
        ListEscrowsRequest body,
        CancellationToken cancellationToken = default)
    {
        var results = new List<EscrowAgreement>();
        var limit = body.Limit ?? _configuration.DefaultListLimit;
        var offset = body.Offset ?? 0;

        int totalCount;

        if (body.PartyId != null)
        {
            var allAgreements = await AgreementStore.QueryAsync(
                a => a.Parties != null && a.Parties.Any(p =>
                    p.PartyId == body.PartyId.Value &&
                    (body.PartyType == null || p.PartyType == body.PartyType)),
                cancellationToken);

            var filtered = allAgreements.AsEnumerable();

            if (body.Status != null && body.Status.Count > 0)
            {
                var statusSet = body.Status.ToHashSet();
                filtered = filtered.Where(a => statusSet.Contains(a.Status));
            }

            var allFiltered = filtered.ToList();
            totalCount = allFiltered.Count;

            results = allFiltered
                .Skip(offset)
                .Take(limit)
                .Select(MapToApiModel)
                .ToList();
        }
        else if (body.Status != null && body.Status.Count > 0)
        {
            var statusSet = body.Status.ToHashSet();
            var allAgreements = await AgreementStore.QueryAsync(
                a => statusSet.Contains(a.Status),
                cancellationToken);

            totalCount = allAgreements.Count;

            results = allAgreements
                .Skip(offset)
                .Take(limit)
                .Select(MapToApiModel)
                .ToList();
        }
        else
        {
            var allAgreements = await AgreementStore.QueryAsync(
                a => true,
                cancellationToken);

            totalCount = allAgreements.Count;

            results = allAgreements
                .Skip(offset)
                .Take(limit)
                .Select(MapToApiModel)
                .ToList();
        }

        return (StatusCodes.OK, new ListEscrowsResponse
        {
            Escrows = results,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Gets a party's token for an escrow.
    /// </summary>
    public async Task<(StatusCodes, GetMyTokenResponse?)> GetMyTokenAsync(
        GetMyTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = GetAgreementKey(body.EscrowId);
        var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

        if (agreementModel == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var party = agreementModel.Parties?.FirstOrDefault(p =>
            p.PartyId == body.OwnerId && p.PartyType == body.OwnerType);

        if (party == null)
        {
            return (StatusCodes.NotFound, null);
        }

        string? token = body.TokenType switch
        {
            TokenType.Deposit => party.DepositToken,
            TokenType.Release => party.ReleaseToken,
            _ => null
        };

        if (token == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var tokenUsed = body.TokenType switch
        {
            TokenType.Deposit => party.DepositTokenUsed,
            TokenType.Release => party.ReleaseTokenUsed,
            _ => false
        };

        var tokenUsedAt = body.TokenType switch
        {
            TokenType.Deposit => party.DepositTokenUsedAt,
            TokenType.Release => party.ReleaseTokenUsedAt,
            _ => (DateTimeOffset?)null
        };

        return (StatusCodes.OK, new GetMyTokenResponse
        {
            Token = token,
            TokenUsed = tokenUsed,
            TokenUsedAt = tokenUsedAt
        });
    }

    #region Input Mapping Helpers

    /// <summary>
    /// Maps asset input to internal model.
    /// </summary>
    private static EscrowAssetModel MapAssetInputToModel(EscrowAssetInput input)
    {
        return new EscrowAssetModel
        {
            AssetType = input.AssetType,
            CurrencyDefinitionId = input.CurrencyDefinitionId,
            CurrencyCode = input.CurrencyCode,
            CurrencyAmount = input.CurrencyAmount,
            ItemInstanceId = input.ItemInstanceId,
            ItemName = input.ItemName,
            ItemTemplateId = input.ItemTemplateId,
            ItemTemplateName = input.ItemTemplateName,
            ItemQuantity = input.ItemQuantity,
            ContractInstanceId = input.ContractInstanceId,
            ContractTemplateCode = input.ContractTemplateCode,
            ContractDescription = input.ContractDescription,
            ContractPartyRole = input.ContractPartyRole,
            CustomAssetType = input.CustomAssetType,
            CustomAssetId = input.CustomAssetId,
            CustomAssetData = input.CustomAssetData
        };
    }

    /// <summary>
    /// Determines if consent is required based on party role.
    /// </summary>
    private static bool DetermineConsentRequired(EscrowPartyRole role)
    {
        return role switch
        {
            EscrowPartyRole.Depositor => true,
            EscrowPartyRole.Recipient => true,
            EscrowPartyRole.DepositorRecipient => true,
            EscrowPartyRole.Arbiter => false,
            EscrowPartyRole.Observer => false,
            _ => false
        };
    }

    #endregion
}
