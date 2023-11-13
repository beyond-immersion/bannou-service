using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Connect;

[ServiceConfiguration(typeof(ConnectService))]
public class ConnectServiceConfiguration : BaseServiceConfiguration
{
    public bool Obfuscate_Not_Found_Response { get; set; } = true;
    public uint Max_Client_Request_Bytes { get; set; } = 16 * 1024;
    public uint Max_Client_Response_Bytes { get; set; } = 16 * 1024;

    [ConfigRequired]
    public string? Redis_Connection_String { get; set; }

    [ConfigRequired]
    public string? Token_Public_Key { get; set; }
}
