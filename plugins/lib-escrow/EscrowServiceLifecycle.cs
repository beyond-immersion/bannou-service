using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

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
    /// <param name="body">The create escrow request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and response with the created escrow.</returns>
    public async Task<(StatusCodes, CreateEscrowResponse?)> CreateEscrowAsync(
        CreateEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate request
            if (body.Parties == null || body.Parties.Count < 2)
            {
                return (StatusCodes.Status400BadRequest, new CreateEscrowResponse
                {
                    Success = false,
                    Error = "At least two parties are required for an escrow agreement"
                });
            }

            if (body.ExpectedDeposits == null || body.ExpectedDeposits.Count == 0)
            {
                return (StatusCodes.Status400BadRequest, new CreateEscrowResponse
                {
                    Success = false,
                    Error = "At least one expected deposit is required"
                });
            }

            // Generate escrow ID
            var escrowId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            // Validate trust mode configuration
            if (body.TrustMode == EscrowTrustMode.Single_party_trusted)
            {
                if (body.TrustedPartyId == null)
                {
                    return (StatusCodes.Status400BadRequest, new CreateEscrowResponse
                    {
                        Success = false,
                        Error = "TrustedPartyId is required for single_party_trusted mode"
                    });
                }
            }

            // Build internal party models with token generation for full_consent mode
            var partyModels = new List<EscrowPartyModel>();
            var tokenRecordsToSave = new List<TokenHashModel>();

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

                // Generate tokens for full_consent mode
                if (body.TrustMode == EscrowTrustMode.Full_consent)
                {
                    // Generate deposit token if party has expected deposits
                    var hasExpectedDeposit = body.ExpectedDeposits.Any(ed =>
                        ed.PartyId == partyInput.PartyId && ed.PartyType == partyInput.PartyType);

                    if (hasExpectedDeposit)
                    {
                        var depositToken = GenerateToken(escrowId, partyInput.PartyId, TokenType.Deposit);
                        partyModel.DepositToken = depositToken;

                        // Store token hash for validation
                        var tokenHash = HashToken(depositToken);
                        tokenRecordsToSave.Add(new TokenHashModel
                        {
                            TokenHash = tokenHash,
                            EscrowId = escrowId,
                            PartyId = partyInput.PartyId,
                            TokenType = TokenType.Deposit,
                            CreatedAt = now,
                            ExpiresAt = body.ExpiresAt,
                            Used = false
                        });
                    }

                    // Generate release token if party requires consent
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
                            ExpiresAt = body.ExpiresAt,
                            Used = false
                        });
                    }
                }

                partyModels.Add(partyModel);
            }

            // Build expected deposit models
            var expectedDepositModels = body.ExpectedDeposits.Select(ed => new ExpectedDepositModel
            {
                PartyId = ed.PartyId,
                PartyType = ed.PartyType,
                ExpectedAssets = ed.ExpectedAssets?.Select(MapAssetInputToModel).ToList(),
                Optional = ed.Optional ?? false,
                DepositDeadline = ed.DepositDeadline,
                Fulfilled = false
            }).ToList();

            // Build release allocation models if provided
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

            // Calculate required consents
            var requiredConsents = body.RequiredConsentsForRelease ?? -1;
            if (requiredConsents == -1)
            {
                // All parties with consentRequired=true must consent
                requiredConsents = partyModels.Count(p => p.ConsentRequired);
            }

            // Build the agreement model
            var agreementModel = new EscrowAgreementModel
            {
                EscrowId = escrowId,
                EscrowType = body.EscrowType,
                TrustMode = body.TrustMode,
                TrustedPartyId = body.TrustedPartyId,
                TrustedPartyType = body.TrustedPartyType,
                InitiatorServiceId = body.InitiatorServiceId,
                Parties = partyModels,
                ExpectedDeposits = expectedDepositModels,
                Deposits = new List<EscrowDepositModel>(),
                ReleaseAllocations = releaseAllocationModels,
                BoundContractId = body.BoundContractId,
                Consents = new List<EscrowConsentModel>(),
                Status = EscrowStatus.Pending_deposits,
                RequiredConsentsForRelease = requiredConsents,
                CreatedAt = now,
                CreatedBy = body.CreatedBy,
                CreatedByType = body.CreatedByType,
                ExpiresAt = body.ExpiresAt,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId,
                Description = body.Description,
                Metadata = body.Metadata
            };

            // Save the agreement
            var agreementKey = GetAgreementKey(escrowId);
            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

            // Save token records
            foreach (var tokenRecord in tokenRecordsToSave)
            {
                var tokenKey = GetTokenKey(tokenRecord.TokenHash);
                await TokenStore.SaveAsync(tokenKey, tokenRecord, cancellationToken);
            }

            // Update status index
            var statusIndexKey = GetStatusIndexKey(EscrowStatus.Pending_deposits);
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = escrowId,
                Status = EscrowStatus.Pending_deposits,
                ExpiresAt = body.ExpiresAt,
                AddedAt = now
            };
            await StatusIndexStore.SaveAsync($"{statusIndexKey}:{escrowId}", statusEntry, cancellationToken);

            // Update party pending counts
            foreach (var party in partyModels)
            {
                var partyKey = GetPartyPendingKey(party.PartyId, party.PartyType);
                var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
                var newCount = new PartyPendingCount
                {
                    PartyId = party.PartyId,
                    PartyType = party.PartyType,
                    PendingCount = (existingCount?.PendingCount ?? 0) + 1,
                    LastUpdated = now
                };
                await PartyPendingStore.SaveAsync(partyKey, newCount, cancellationToken);
            }

            // Build party tokens for response
            var partyTokens = partyModels
                .Where(p => p.DepositToken != null || p.ReleaseToken != null)
                .Select(p => new PartyToken
                {
                    PartyId = p.PartyId,
                    PartyType = p.PartyType,
                    DepositToken = p.DepositToken,
                    ReleaseToken = p.ReleaseToken
                })
                .ToList();

            // Map to API model
            var escrowAgreement = MapToApiModel(agreementModel);

            // Publish creation event
            var createdEvent = new EscrowCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = escrowId,
                EscrowType = agreementModel.EscrowType.ToString(),
                TrustMode = agreementModel.TrustMode.ToString(),
                Parties = partyModels.Select(p => new EscrowPartyInfo
                {
                    PartyId = p.PartyId,
                    PartyType = p.PartyType,
                    Role = p.Role.ToString()
                }).ToList(),
                ExpectedDepositCount = expectedDepositModels.Count,
                ExpiresAt = body.ExpiresAt,
                BoundContractId = body.BoundContractId,
                ReferenceType = body.ReferenceType,
                ReferenceId = body.ReferenceId,
                CreatedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowCreated, createdEvent, cancellationToken);

            _logger.LogInformation(
                "Created escrow {EscrowId} with {PartyCount} parties, type {EscrowType}, trust mode {TrustMode}",
                escrowId, partyModels.Count, body.EscrowType, body.TrustMode);

            return (StatusCodes.Status200OK, new CreateEscrowResponse
            {
                Success = true,
                Escrow = escrowAgreement,
                PartyTokens = partyTokens
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create escrow");
            await EmitErrorAsync("CreateEscrow", ex.Message, cancellationToken: cancellationToken);
            return (StatusCodes.Status500InternalServerError, new CreateEscrowResponse
            {
                Success = false,
                Error = "An unexpected error occurred while creating the escrow"
            });
        }
    }

    /// <summary>
    /// Gets an escrow agreement by ID.
    /// </summary>
    /// <param name="body">The get escrow request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and response with the escrow.</returns>
    public async Task<(StatusCodes, GetEscrowResponse?)> GetEscrowAsync(
        GetEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new GetEscrowResponse
                {
                    Found = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var escrowAgreement = MapToApiModel(agreementModel);

            return (StatusCodes.Status200OK, new GetEscrowResponse
            {
                Found = true,
                Escrow = escrowAgreement
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("GetEscrow", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new GetEscrowResponse
            {
                Found = false,
                Error = "An unexpected error occurred while retrieving the escrow"
            });
        }
    }

    /// <summary>
    /// Lists escrow agreements with optional filtering.
    /// </summary>
    /// <param name="body">The list escrows request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and response with the list of escrows.</returns>
    public async Task<(StatusCodes, ListEscrowsResponse?)> ListEscrowsAsync(
        ListEscrowsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var results = new List<EscrowAgreement>();
            var limit = body.Limit ?? 50;
            var offset = body.Offset ?? 0;

            // If filtering by status, use the status index
            if (body.StatusFilter != null && body.StatusFilter.Count > 0)
            {
                foreach (var status in body.StatusFilter)
                {
                    var statusIndexKey = GetStatusIndexKey(status);
                    // Query status index entries - this is a simplified approach
                    // In production, we'd use a proper query with pagination
                    var entries = await StatusIndexStore.QueryAsync<StatusIndexEntry>(
                        $"$.Status == \"{status}\"",
                        limit,
                        offset,
                        cancellationToken);

                    foreach (var entry in entries)
                    {
                        var agreementKey = GetAgreementKey(entry.EscrowId);
                        var model = await AgreementStore.GetAsync(agreementKey, cancellationToken);
                        if (model != null)
                        {
                            // Apply party filter if specified
                            if (body.PartyId != null)
                            {
                                var hasParty = model.Parties?.Any(p =>
                                    p.PartyId == body.PartyId &&
                                    (body.PartyType == null || p.PartyType == body.PartyType)) ?? false;

                                if (!hasParty) continue;
                            }

                            results.Add(MapToApiModel(model));
                        }
                    }
                }
            }
            else if (body.PartyId != null)
            {
                // Query by party - use party pending index or full scan
                // This is a simplified implementation
                var partyKey = GetPartyPendingKey(body.PartyId.Value, body.PartyType ?? "account");
                var partyPending = await PartyPendingStore.GetAsync(partyKey, cancellationToken);

                if (partyPending != null && partyPending.PendingCount > 0)
                {
                    // Need to scan agreements - in production use proper indexing
                    var allAgreements = await AgreementStore.QueryAsync<EscrowAgreementModel>(
                        "$",
                        limit + offset,
                        0,
                        cancellationToken);

                    foreach (var model in allAgreements.Skip(offset).Take(limit))
                    {
                        var hasParty = model.Parties?.Any(p =>
                            p.PartyId == body.PartyId &&
                            (body.PartyType == null || p.PartyType == body.PartyType)) ?? false;

                        if (hasParty)
                        {
                            results.Add(MapToApiModel(model));
                        }
                    }
                }
            }
            else
            {
                // No filters - return all with pagination
                var allAgreements = await AgreementStore.QueryAsync<EscrowAgreementModel>(
                    "$",
                    limit,
                    offset,
                    cancellationToken);

                results = allAgreements.Select(MapToApiModel).ToList();
            }

            return (StatusCodes.Status200OK, new ListEscrowsResponse
            {
                Escrows = results,
                TotalCount = results.Count,
                HasMore = results.Count >= limit
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list escrows");
            await EmitErrorAsync("ListEscrows", ex.Message, cancellationToken: cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ListEscrowsResponse
            {
                Escrows = new List<EscrowAgreement>(),
                TotalCount = 0,
                HasMore = false
            });
        }
    }

    /// <summary>
    /// Gets a party's token for an escrow (if they have permission).
    /// </summary>
    /// <param name="body">The get token request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and response with the token.</returns>
    public async Task<(StatusCodes, GetMyTokenResponse?)> GetMyTokenAsync(
        GetMyTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new GetMyTokenResponse
                {
                    Found = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            // Find the party
            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                return (StatusCodes.Status404NotFound, new GetMyTokenResponse
                {
                    Found = false,
                    Error = "Party not found in this escrow"
                });
            }

            // Get the requested token
            string? token = body.TokenType switch
            {
                TokenType.Deposit => party.DepositToken,
                TokenType.Release => party.ReleaseToken,
                _ => null
            };

            if (token == null)
            {
                return (StatusCodes.Status404NotFound, new GetMyTokenResponse
                {
                    Found = false,
                    Error = $"No {body.TokenType} token exists for this party"
                });
            }

            // Check if already used
            var alreadyUsed = body.TokenType switch
            {
                TokenType.Deposit => party.DepositTokenUsed,
                TokenType.Release => party.ReleaseTokenUsed,
                _ => false
            };

            return (StatusCodes.Status200OK, new GetMyTokenResponse
            {
                Found = true,
                Token = token,
                TokenType = body.TokenType,
                AlreadyUsed = alreadyUsed
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get token for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("GetMyToken", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new GetMyTokenResponse
            {
                Found = false,
                Error = "An unexpected error occurred while retrieving the token"
            });
        }
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
            CustomAssetData = input.CustomAssetData,
            SourceOwnerId = input.SourceOwnerId,
            SourceOwnerType = input.SourceOwnerType,
            SourceContainerId = input.SourceContainerId
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
            EscrowPartyRole.Depositor_recipient => true,
            EscrowPartyRole.Arbiter => false,
            EscrowPartyRole.Observer => false,
            _ => false
        };
    }

    #endregion
}
